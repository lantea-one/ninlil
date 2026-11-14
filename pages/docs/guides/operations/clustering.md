# Clustering, events and webhooks

This page lets you join two or more Ninlil servers into a cluster, carry events to other systems
through a message broker, and send events to a URL with webhooks. The README holds the complete
reference, including every behaviour of the backfill:
[Clustering, events and webhooks](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#clustering-events-and-webhooks).

- A **cluster** joins Ninlil servers, called **nodes**. Every node keeps its own database, caches and
  DNS answers, and serves queries by itself. The nodes exchange heartbeats and push configuration
  changes to each other. Clustering is off by default.
- **Events** are messages Ninlil publishes inside the process when something happens, such as a zone
  change. An optional **event broker** (RabbitMQ or ActiveMQ) carries them to other Ninlil instances
  and to other programs.
- **Webhooks** send seven kinds of event to a URL you choose, as an HTTP `POST` with a JSON body.
  Failed deliveries are retried and recorded.

A cluster copies users and roles (with password hashes and two-factor secrets), filter lists, access
control rules, forwarders, global rewrites, five settings sections and deletion records. It does not
copy zones and records (use [zone transfers](../dns/secondary-zones.md)), refresh and API tokens,
webhook subscriptions, DHCP data, clients, TLS certificates or the JWT signing key.

## Before you start

1. Two or more Ninlil servers, all on one version, each able to reach the web HTTPS port of the
   others. Check from one node (`<node-b>` is the other node's name or address; `-k` accepts the
   self-signed certificate):

    ```bash
    curl -k -s -o /dev/null -w '%{http_code}\n' https://<node-b>/status
    ```

    ```text
    200
    ```

2. An account on each node with `clustersettings:write` and `clusternodes:write` (the bootstrap
   administrator has both) and its access token. The commands call the tokens `<token-a>` and
   `<token-b>`. Sign in as the [quick start](../../getting-started/quick-start.md) shows.
3. One random value for the JWT signing key, used on every node:

    ```bash
    openssl rand -base64 48
    ```

    The commands call it `<jwt-key>` (your value differs).
4. For a broker: a RabbitMQ or ActiveMQ Classic server every node reaches. For webhooks: a web address
   that accepts `POST` requests with a JSON body.
5. On the node that will join (node B), the ids of its own `admin` account and `Administrators` role.
   After the join, two accounts share each name:

    ```bash
    curl -k https://<node-b>/api/users -H 'Authorization: Bearer <token-b>'
    curl -k https://<node-b>/api/roles -H 'Authorization: Bearer <token-b>'
    ```

    Note the `id` of each. The commands call them `<bootstrap-user-id>` and `<bootstrap-role-id>`.

The commands that name a node (`<node-a>`, `<node-b>`) call another machine with `curl -k`, which
skips the certificate check, so anyone in the path could pose as that node and read the token. Run
them only on a network you trust, or install a trusted certificate on each node and leave `-k` out.
The other examples call `127.0.0.1`; see
[Self-signed certificate](../../getting-started/quick-start.md#self-signed-certificate).

## Form a cluster

Node A invites, node B joins.

1. On every node, set the same JWT signing key and restart the node
   ([`NINLIL_JWT_SIGNING_KEY`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#identity-authentication--sso)).
   For a container add `-e NINLIL_JWT_SIGNING_KEY=<jwt-key>`, for a Linux package put
   `NINLIL_JWT_SIGNING_KEY=<jwt-key>` in `/etc/ninlil/ninlil.env`. Ninlil checks an access token's
   signature with this key, so without the same key a client that moves between nodes gets `401`.
   Use at least 32 bytes: Ninlil refuses to start with a key shorter than 16 bytes.
2. On every node, switch clustering on and tell the node which address its peers use. Run this on
   node A, and again on node B with its own name (`<node-a>` is the name or address peers use, without
   `https://`):

    ```bash
    curl -k -X PATCH https://<node-a>/api/cluster-settings -H 'Authorization: Bearer <token-a>' -H 'Content-Type: application/json' -d '{"enable":true,"advertisedHost":"<node-a>"}'
    ```

    ```text
    {"note":"Written to the configuration file. This setting is read live, so it takes effect immediately -- no restart required."}
    ```

    If the HTTPS port is not 443, add `"advertisedPort":<port>`. Wait a few seconds before step 3:
    Ninlil reloads its configuration file first, and an invite sent at once can answer
    `Clustering is not enabled on this node`.
3. On node A, create an invite:

    ```bash
    curl -k -X POST https://<node-a>/api/cluster/nodes/invite -H 'Authorization: Bearer <token-a>'
    ```

    ```text
    {"inviteCode":"ninlil-cluster-invite-1.eyJWZXJzaW9uIjoxLC...","expiresAtUtc":"..."}
    ```

    Copy the `inviteCode`. It holds the secret of a TSIG key, so treat it like a password and send it over a
    private channel. It works for 15 minutes.
4. On node B, join with that code. The display name is optional:

    ```bash
    curl -k -X POST https://<node-b>/api/cluster/nodes/join -H 'Authorization: Bearer <token-b>' -H 'Content-Type: application/json' -d '{"inviteCode":"<invite-code>","displayName":"office"}'
    ```

    ```text
    {"id":"...","name":"office","remoteNodeId":"...","host":"<node-a>","port":443,"status":"Joining", ...
    ```

    The console does the same on the **Cluster** page (**Invite a peer** and **Join a cluster**).
5. Wait for the backfill: after the next heartbeat that reaches node B (30 seconds by default), node A
   sends node B everything in the list above. Zones, records and DHCP data are not copied.
6. Delete node B's own bootstrap account and role, after the backfill has finished and only when node B
   holds nothing you want to keep. Use `<token-a>`, which works on node B because both nodes share the
   key and node A's administrator exists there:

    ```bash
    curl -k -X DELETE https://<node-b>/api/users/<bootstrap-user-id> -H 'Authorization: Bearer <token-a>'
    curl -k -X DELETE https://<node-b>/api/roles/<bootstrap-role-id> -H 'Authorization: Bearer <token-a>'
    ```

    Each answers `204`. Before the delete, node B lists both pairs: the pair that lost the
    newest-write comparison is renamed to `admin-conflict-<node-id>` (and `Administrators-conflict-<node-id>`)
    and carries a `clusterConflictNote`. If node A's pair was the one renamed, it keeps that name on node
    B. A user name cannot be edited, so sign in as the renamed account with node A's password, or create
    an account with the plain name and move the roles to it.

### Check that it worked

1. The peer is `Healthy` within one heartbeat:

    ```bash
    curl -k https://<node-a>/api/cluster/nodes -H 'Authorization: Bearer <token-a>'
    ```

    ```text
    {"items":[{"id":"...","name":"office","remoteNodeId":"...","host":"<node-b>","port":443,"status":"Healthy","lastHeartbeatAtUtc":"...","missedHeartbeatCount":0, ...}],"skip":0,"take":50,"totalCount":1}
    ```

2. A setting the cluster copies reaches the other node. Switch the rewrite engine on at node A, then
   read it at node B:

    ```bash
    curl -k -X PATCH https://<node-a>/api/rewrite-settings -H 'Authorization: Bearer <token-a>' -H 'Content-Type: application/json' -d '{"enable":true}'
    curl -k https://<node-b>/api/rewrite-settings -H 'Authorization: Bearer <token-b>'
    ```

    ```text
    {"enable":true,"timeToLiveSeconds":300,"lastModifiedUtc":"...","originatingNodeId":"..."}
    ```

3. Node A's log shows the backfill (for a container use `docker logs <container>`):

    ```bash
    journalctl -u ninlil | grep 'Config backfill'
    ```

    ```text
    Config backfill to peer node 'office' started.
    Config backfill to peer node 'office' done: 4 items delivered.
    ```

    (your count differs).
4. A token from node A works on node B:

    ```bash
    curl -k -s -o /dev/null -w '%{http_code}\n' https://<node-b>/api/zones -H 'Authorization: Bearer <token-a>'
    ```

    ```text
    200
    ```

    With different keys it is `401`.

## Switch on an event broker

Optional. Do this on every node that should share events.

1. Set the transport and the connection in the environment of each node's Ninlil process, then restart
   it. The console cannot switch the transport live. For RabbitMQ (for ActiveMQ use `ActiveMq` and the
   `NINLIL_DNS_SERVER_EVENTS_ACTIVE_MQ_*` variables, all listed in
   [Event broker transports](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#event-broker-message-queue-transports)):

    ```text
    NINLIL_DNS_SERVER_EVENTS_TRANSPORT=RabbitMq
    NINLIL_DNS_SERVER_EVENTS_RABBIT_MQ_HOST_NAME=<broker-host>
    NINLIL_DNS_SERVER_EVENTS_RABBIT_MQ_USER_NAME=<user>
    NINLIL_DNS_SERVER_EVENTS_RABBIT_MQ_PASSWORD=<password>
    ```

2. Look for the connection line in the log:

    ```text
    Connected to the RabbitMQ event broker at <broker-host>:5672.
    ```

   `GET /status` (signed in) shows the `eventBroker` component as `Connected to the RabbitMq event broker.`
   The broker carries every event except settings sync, which holds secrets and uses the cluster
   channel. An instance acts only on events it made: a zone change from another instance does not start
   this instance's webhooks.

## Create a webhook

1. Start a receiver. For a test, save this as `receiver.py` and run `python3 receiver.py` in a second
   terminal. It prints each request and answers `204`:

    ```python
    import http.server


    class Receiver(http.server.BaseHTTPRequestHandler):
        def do_POST(self):
            body = self.rfile.read(int(self.headers.get("Content-Length", 0)))
            print(self.headers.get("Authorization"), self.headers.get("X-Ninlil-Signature"))
            print(body.decode(), flush=True)
            self.send_response(204)
            self.send_header("Content-Length", "0")
            self.end_headers()

        def log_message(self, *args):
            return


    http.server.HTTPServer(("127.0.0.1", 8099), Receiver).serve_forever()
    ```

2. Create the subscription (permission `webhooks:write`). Subscriptions are not copied between nodes, so
   repeat this on each node that should send. `BearerToken` is the secret your receiver checks in
   the `Authorization` header:

    ```bash
    curl -k -X POST https://<node-a>/api/webhooks -H 'Authorization: Bearer <token-a>' -H 'Content-Type: application/json' -d '{"name":"zone-changes","targetUrl":"http://127.0.0.1:8099/hook","eventTypeNames":["DnsZoneChanged"],"authMode":"Bearer","authCredentials":{"BearerToken":"demo-token"},"enableHmacSigning":true,"maxDeliveryAttempts":3}'
    ```

    ```text
    {"subscription":{"id":"<webhook-id>","name":"zone-changes", ...},"authCredentials":{"BearerToken":"demo-token"},"hmacSigningSecret":"1hp++cErLc4L..."}
    ```

    Copy the `id` and the `hmacSigningSecret`. This is the only time Ninlil shows the secret. The
    console's **Webhooks** page does the same.
3. Trigger an event by creating a zone:

    ```bash
    curl -k -X POST https://<node-a>/api/zones -H 'Authorization: Bearer <token-a>' -H 'Content-Type: application/json' -d '{"name":"hook.test","type":"Primary"}'
    ```

4. Read the receiver's terminal. The first line holds the `Authorization` and `X-Ninlil-Signature`
   headers, the second the body:

    ```text
    Bearer demo-token 7511dff72e60152049c5ab40e7f4dd851f149f67b5c5af699eac3f5a49b3e5fe
    {"eventType":"DnsZoneChanged","deliveredAtUtc":"...","data":{"occurredAtUtc":"...","zoneId":"...","zoneName":"hook.test"}}
    ```

5. Check the signature. The header is the lowercase hexadecimal HMAC-SHA256 of the body, keyed with the
   secret. Paste the body line between the quotes:

    ```bash
    printf '%s' '<body>' | openssl dgst -sha256 -hmac '<hmac-signing-secret>'
    ```

    The value equals the `X-Ninlil-Signature` header.
6. The delivery shows as `Succeeded`:

    ```bash
    curl -k https://<node-a>/api/webhooks/<webhook-id>/delivery-attempts -H 'Authorization: Bearer <token-a>'
    ```

    ```text
    {"items":[{"id":"...","subscriptionName":"zone-changes","eventTypeName":"DnsZoneChanged", ... "status":"Succeeded","attemptCount":1,"maxAttempts":3, ...}],"skip":0,"take":50,"totalCount":1}
    ```

The seven events a webhook can deliver are `DnsZoneChanged`, `DnsCacheEvicted`,
`DnssecZoneResigningCompleted`, `AcmeCertificateRenewalSucceeded`, `AcmeCertificateRenewalFailed`,
`CloudDnsZoneSyncFailed` and `TrustAnchorRollover`. Any other name is refused:

```text
'Nope' is not an event type webhooks deliver. Accepted: DnsZoneChanged, DnsCacheEvicted, DnssecZoneResigningCompleted, AcmeCertificateRenewalSucceeded, AcmeCertificateRenewalFailed, CloudDnsZoneSyncFailed, TrustAnchorRollover.
```

After a failed delivery Ninlil waits one second, then two, then four, up to 30 seconds, and tries up to
`maxDeliveryAttempts` times (5 when you give none). The delivery is then `DeadLettered`. A webhook can
reach any address the server can, so give `webhooks:write` only to people you trust. The signing secret
cannot be changed: delete the subscription and create it again to rotate it.

## Settings

| Setting | Variable and console page |
|---|---|
| Clustering, this node's address, heartbeat | `NINLIL_CLUSTER_*` ([rows](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#webhooks-backup-self-update--clustering)). **Settings**, **Cluster**. |
| Token signing key | `NINLIL_JWT_SIGNING_KEY` |
| Event transport | `NINLIL_DNS_SERVER_EVENTS_TRANSPORT` |
| Broker connection | [Event broker rows](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#event-broker-message-queue-transports). **Settings**, **Event broker**. |
| Webhook delivery timing | `NINLIL_DNS_SERVER_WEBHOOKS_DELIVERY_*`. **Settings**, **Webhook Delivery**. |

## When something goes wrong

- *Creating an invite answers `Clustering is not enabled on this node -- set 'Cluster:Enable' before inviting a peer.`* Run step 2 on that node and wait a few seconds.
- *It answers `This node's own 'Cluster:AdvertisedHost' has not been configured -- a peer would have no way to reach it.`* Set `advertisedHost` (step 2).
- *Joining answers `This does not look like a Ninlil cluster invite code.`* The code was cut or changed on the way. Copy the whole `inviteCode`.
- *Joining answers `409` with `This node is already a member of that peer's cluster.`* List the nodes with `GET /api/cluster/nodes`.
- *Joining fails because the inviter cannot be reached, or the code has expired.* Check that node B reaches node A's address and port, create another invite and join again. A failed join leaves nothing behind.
- *A peer stays `Unreachable`.* Check that the peer runs and that its `host` and `port` reach its HTTPS port from this node. It turns `Healthy` at the next heartbeat after it answers.
- *A token from one node gets `401` on another.* The nodes use different `NINLIL_JWT_SIGNING_KEY`, `NINLIL_JWT_ISSUER` or `NINLIL_JWT_AUDIENCE` values. Set the same on every node and restart.
- *A webhook delivery is `DeadLettered`.* Read `lastErrorMessage` and `lastHttpStatusCode` in the delivery attempt. The receiver must answer with a status from 200 to 299.
- *A webhook never fires.* A subscription fires only for events this instance produced. Check that `isDisabled` is `false` and the event name is in `eventTypeNames`.

## Next steps

- [Security](../security/security.md): the cluster sends some secrets over its channel.
- [Storage](storage.md): read replicas and cluster refresh delays.
- README: [Behaviour worth knowing](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#behaviour-worth-knowing-12)
  for the backfill, cache delays and event limits.
