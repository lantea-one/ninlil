#!/usr/bin/env bash
#
# Checks by hand that the Ninlil DHCP relay sends its replies out the client's link. It builds
# the case where this goes wrong: a relay with two network links whose default route points at
# the server side, so a reply sent "wherever the routing table says" would leave on the wrong
# link. Not run in CI: it needs root, Linux network namespaces, tcpdump and external DHCP
# clients, and it starts two full Ninlil instances.
#
#   src/script/development/dhcp-relay-two-link-test.sh /path/to/Ninlil.Web [--keep]
#
# Arguments:
#   the first argument is the path of a Ninlil executable (a published or packaged Ninlil.Web).
#                 Its whole directory is copied once per instance, so each instance gets its own
#                 data directory (Ninlil keeps its data under "data/" next to the executable).
#   --keep        keeps the namespaces, the instance directories and the capture afterwards.
#
# Needs: root, ip (iproute2), tcpdump, curl, a DHCPv4 client (busybox udhcpc or dhclient) and a
# DHCPv6 client (dhclient or odhcp6c). The clients are only run, never read.
#
# What it builds (three network namespaces nl-srv, nl-rly and nl-cli):
#   nl-srv <-> nl-rly   10.99.1.0/24 and fd99:1::/64   the server side (server .1, relay .2)
#   nl-rly <-> nl-cli   10.99.2.0/24 and fd99:2::/64   the client side (relay .1, client by DHCP)
# The relay's default route points at the server side.
# A Ninlil DHCP engine runs in nl-srv. A Ninlil DHCP relay (giaddr 10.99.2.1, link-address
# fd99:2::1, upstream servers 10.99.1.1 and fd99:1::1) runs in nl-rly.
#
# Manual step: the server needs a DHCPv4 scope for 10.99.2.0/24 and a DHCPv6 scope for
# fd99:2::/64. The script cannot create them, so it prints the address of the server's admin
# console and waits for Enter. The bootstrap administrator credentials are in ENVIRONMENT.md.
#
# Output: progress lines, then the tcpdump capture of the relay's client-side link
# (<work directory>/client-side.txt) and one line per check, ending in PASS or FAIL.
# Exit status: 0 on PASS, 1 on FAIL, 2 on a missing prerequisite or bad arguments.
# PASS means all of: the DHCPv4 client got an address in 10.99.2.0/24, the DHCPv4 reply left the
# client-side link from 10.99.2.1, the DHCPv6 client got an address in fd99:2::/64, and a DHCPv6
# reply reached the client's link-local address.

set -euo pipefail

keep=false
executable=''
for argument in "$@"; do
    case "${argument}" in
        --keep) keep=true ;;
        *) executable="${argument}" ;;
    esac
done

if [[ -z "${executable}" || ! -x "${executable}" ]]; then
    echo "Usage: $0 /path/to/Ninlil.Web [--keep]" >&2
    exit 2
fi
if [[ "${EUID}" -ne 0 ]]; then
    echo 'This test needs root (it creates network namespaces).' >&2
    exit 2
fi
for tool in ip tcpdump curl; do
    if ! command -v "${tool}" > /dev/null; then
        echo "Missing prerequisite: ${tool}" >&2
        exit 2
    fi
done

dhcpv4_client=''
if command -v udhcpc > /dev/null; then
    dhcpv4_client='udhcpc'
elif command -v dhclient > /dev/null; then
    dhcpv4_client='dhclient'
fi
dhcpv6_client=''
if command -v dhclient > /dev/null; then
    dhcpv6_client='dhclient'
elif command -v odhcp6c > /dev/null; then
    dhcpv6_client='odhcp6c'
fi
if [[ -z "${dhcpv4_client}" || -z "${dhcpv6_client}" ]]; then
    echo 'Missing a DHCPv4 client (udhcpc or dhclient) or a DHCPv6 client (dhclient or odhcp6c).' >&2
    exit 2
fi

work="$(mktemp -d)"
executable_directory="$(cd "$(dirname "${executable}")" && pwd)"
executable_name="$(basename "${executable}")"
pids=()

cleanup() {
    for pid in "${pids[@]}"; do
        kill "${pid}" 2> /dev/null || true
    done
    if [[ "${keep}" == true ]]; then
        echo "Kept: namespaces nl-srv nl-rly nl-cli and ${work}"
        return
    fi
    for namespace in nl-srv nl-rly nl-cli; do
        ip netns del "${namespace}" 2> /dev/null || true
    done
    rm -rf "${work}"
}
trap cleanup EXIT

echo '== building the namespaces'
for namespace in nl-srv nl-rly nl-cli; do
    ip netns add "${namespace}"
    ip -n "${namespace}" link set lo up
done
ip link add nlsr0 type veth peer name nlrs0
ip link set nlsr0 netns nl-srv
ip link set nlrs0 netns nl-rly
ip link add nlrc0 type veth peer name nlcr0
ip link set nlrc0 netns nl-rly
ip link set nlcr0 netns nl-cli

ip -n nl-srv addr add 10.99.1.1/24 dev nlsr0
ip -n nl-srv addr add fd99:1::1/64 dev nlsr0 nodad
ip -n nl-rly addr add 10.99.1.2/24 dev nlrs0
ip -n nl-rly addr add fd99:1::2/64 dev nlrs0 nodad
ip -n nl-rly addr add 10.99.2.1/24 dev nlrc0
ip -n nl-rly addr add fd99:2::1/64 dev nlrc0 nodad
ip -n nl-srv link set nlsr0 up
ip -n nl-rly link set nlrs0 up
ip -n nl-rly link set nlrc0 up
ip -n nl-cli link set nlcr0 up

# The server answers to giaddr 10.99.2.1, so it needs a way back to the client side. The relay's
# default route points at the server side; this is what makes the wrong-link case happen.
ip -n nl-srv route add 10.99.2.0/24 via 10.99.1.2
ip -n nl-srv -6 route add fd99:2::/64 via fd99:1::2
ip -n nl-rly route add default via 10.99.1.1
ip -n nl-rly -6 route add default via fd99:1::1

echo '== starting the Ninlil instances'
for instance in server relay; do
    mkdir -p "${work}/${instance}"
    cp -r "${executable_directory}/." "${work}/${instance}/"
done

# Each instance runs in its own namespace, so the default ports do not clash. The web port is
# moved off 80 and 443 only to stay clear of anything else on the machine.
(
    cd "${work}/server"
    ip netns exec nl-srv env \
        NINLIL_DNS_SERVER_WEB_HTTP_PORT=18080 \
        NINLIL_DNS_SERVER_WEB_HTTPS_PORT=18443 \
        NINLIL_DHCP_SERVER_PROTOCOL_DHCPV4_ENABLE=true \
        NINLIL_DHCP_SERVER_PROTOCOL_DHCPV6_ENABLE=true \
        "./${executable_name}" > "${work}/server.log" 2>&1
) &
pids+=("$!")
# The upstream server lists have no environment variable (ENVIRONMENT.md), so they are passed on
# the command line.
(
    cd "${work}/relay"
    ip netns exec nl-rly env \
        NINLIL_DNS_SERVER_WEB_HTTP_PORT=18080 \
        NINLIL_DNS_SERVER_WEB_HTTPS_PORT=18443 \
        NINLIL_DHCP_SERVER_PROTOCOL_DHCPV4_ENABLE=true \
        NINLIL_DHCP_SERVER_PROTOCOL_DHCPV6_ENABLE=true \
        NINLIL_DHCP_SERVER_RELAY_DHCPV4_ENABLE=true \
        NINLIL_DHCP_SERVER_RELAY_DHCPV4_RELAY_AGENT_ADDRESS=10.99.2.1 \
        NINLIL_DHCP_SERVER_RELAY_DHCPV6_ENABLE=true \
        NINLIL_DHCP_SERVER_RELAY_DHCPV6_RELAY_LINK_ADDRESS=fd99:2::1 \
        "./${executable_name}" \
        --DhcpServer:Relay:Dhcpv4:UpstreamServerAddresses:0=10.99.1.1 \
        --DhcpServer:Relay:Dhcpv6:UpstreamServerAddresses:0=fd99:1::1 \
        > "${work}/relay.log" 2>&1
) &
pids+=("$!")

# Wait until the server's web port answers.
ready=false
for _ in $(seq 1 60); do
    if ip netns exec nl-srv curl -s -o /dev/null http://10.99.1.1:18080/; then
        ready=true
        break
    fi
    sleep 1
done
if [[ "${ready}" != true ]]; then
    echo "The server instance did not start; see ${work}/server.log" >&2
    keep=true
    exit 1
fi

echo
echo 'Manual step: create the scopes in the server instance.'
echo '  Admin console: http://10.99.1.1:18080/ (reach it with: ip netns exec nl-srv curl ...,'
echo '  or from a browser started in the namespace).'
echo '  DHCPv4 scope: 10.99.2.0/24, range 10.99.2.100 to 10.99.2.200, router 10.99.2.1.'
echo '  DHCPv6 scope: fd99:2::/64, range fd99:2::100 to fd99:2::200.'
read -r -p 'Press Enter when both scopes exist. ' _

echo "== capturing the relay's client-side link"
ip netns exec nl-rly tcpdump -n -l -i nlrc0 'udp port 67 or udp port 68 or udp port 546 or udp port 547' \
    > "${work}/client-side.txt" 2> /dev/null &
pids+=("$!")
sleep 1

echo '== running the DHCPv4 client'
if [[ "${dhcpv4_client}" == udhcpc ]]; then
    ip netns exec nl-cli udhcpc -i nlcr0 -n -q -f -t 5 -T 3 || true
else
    ip netns exec nl-cli dhclient -1 -v nlcr0 || true
fi

echo '== running the DHCPv6 client'
# Give the client's link-local address a moment to leave the tentative state.
sleep 2
if [[ "${dhcpv6_client}" == dhclient ]]; then
    ip netns exec nl-cli dhclient -6 -1 -v nlcr0 || true
else
    ip netns exec nl-cli timeout 20 odhcp6c -v nlcr0 || true
fi
sleep 1

echo '== checks'
failed=0
check() {
    local description="$1"
    shift
    if "$@"; then
        echo "ok:   ${description}"
    else
        echo "FAIL: ${description}"
        failed=1
    fi
}
client_has_address() {
    ip -n nl-cli addr show dev nlcr0 | grep -Eq "$1"
}
capture_has() {
    grep -Eq "$1" "${work}/client-side.txt"
}

check 'the DHCPv4 client got an address in 10.99.2.0/24' \
    client_has_address 'inet 10\.99\.2\.'
check 'a DHCPv4 reply left the client-side link from 10.99.2.1' \
    capture_has '10\.99\.2\.1\.[0-9]+ > .*\.68: BOOTP/DHCP, Reply'
check 'the DHCPv6 client got an address in fd99:2::/64' \
    client_has_address 'inet6 fd99:2:'
check 'a DHCPv6 reply reached the client link-local address' \
    capture_has ' > fe80:[0-9a-f:]*\.546: dhcp6 reply'

if [[ "${failed}" -eq 0 ]]; then
    echo 'PASS'
    exit 0
fi
echo "FAIL (capture: ${work}/client-side.txt, logs: ${work}/server.log and ${work}/relay.log)"
keep=true
exit 1
