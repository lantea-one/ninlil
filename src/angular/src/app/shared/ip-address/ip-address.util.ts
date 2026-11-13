import { AbstractControl, ValidationErrors } from '@angular/forms';

/** This function reports whether a text is a dotted-decimal IPv4 address (four octets, 0 to 255). */
function isDottedQuad(text: string): boolean {
  const octets = text.split('.');
  return octets.length === 4 && octets.every((octet) => /^\d{1,3}$/.test(octet) && +octet <= 255);
}

/**
 * This function reports whether a text is an IPv6 address in one of the three conventional text
 * forms of RFC 4291 §2.2: the full form `x:x:x:x:x:x:x:x`, the compressed form with one `::`, and
 * the form with a dotted-quad IPv4 tail (`::ffff:192.0.2.1`, `64:ff9b::192.0.2.33`). Each group is
 * one to four hex digits. A zone index (`fe80::1%2`) is not part of the address text and is
 * refused, as the records API refuses it. The AAAA form and its validator use this function.
 */
export function isIpv6Address(text: string): boolean {
  if (!text || !/^[0-9a-fA-F:.]+$/.test(text)) {
    return false;
  }

  // "::" stands for one or more zero groups and may appear once.
  const halves = text.split('::');
  if (halves.length > 2) {
    return false;
  }

  const groups = halves.flatMap((half) => (half.length > 0 ? half.split(':') : []));
  let groupCount = 0;

  for (let index = 0; index < groups.length; index++) {
    const group = groups[index];
    const isLast = index === groups.length - 1;

    if (group.includes('.')) {
      // The dotted quad takes the place of the two lowest groups, so it can only end the address.
      if (!isLast || !isDottedQuad(group)) {
        return false;
      }
      groupCount += 2;
    } else if (/^[0-9a-fA-F]{1,4}$/.test(group)) {
      groupCount += 1;
    } else {
      return false;
    }
  }

  // Without "::" all eight groups are written; with it at least one zero group is left to stand for.
  return halves.length === 1 ? groupCount === 8 : groupCount <= 7;
}

/**
 * This function parses an IPv6 address into its 16 octets, high byte first. It takes the same text
 * forms as {@link isIpv6Address} (RFC 4291 §2.2) and throws an `Error` for any other text. A
 * dotted-quad IPv4 tail is allowed only as the last 32 bits (`::ffff:192.0.2.1`), where its four
 * decimal parts become the last four octets. The APL CIDR and DHCP option value encoders call it,
 * so both accept the same addresses.
 */
export function parseIpv6(address: string): number[] {
  const invalid = (): Error => new Error(`"${address}" is not a valid IPv6 address.`);
  let text = address;

  // The dotted quad becomes two hex groups before the "::" expansion, so it counts as two groups.
  if (text.includes('.')) {
    const lastColon = text.lastIndexOf(':');
    const octetText = text.slice(lastColon + 1);
    const octets = octetText.split('.');

    if (lastColon < 0 || !octets.every((octet) => /^\d{1,3}$/.test(octet) && +octet <= 255)) {
      throw invalid();
    }

    if (octets.length !== 4) {
      throw invalid();
    }

    const [high, low] = [(+octets[0] << 8) | +octets[1], (+octets[2] << 8) | +octets[3]];
    text = `${text.slice(0, lastColon + 1)}${high.toString(16)}:${low.toString(16)}`;
  }

  // Any dot left here sits outside the tail, which is not allowed.
  if (text.includes('.')) {
    throw invalid();
  }

  // "::" stands for one or more zero groups and may appear once.
  const halves = text.split('::');

  if (halves.length > 2) {
    throw invalid();
  }

  const head = halves[0].length > 0 ? halves[0].split(':') : [];
  const tail = halves.length === 2 && halves[1].length > 0 ? halves[1].split(':') : [];
  const missingGroupCount = 8 - head.length - tail.length;

  // Without "::" all eight groups are written; with it at least one zero group must stand for.
  if (halves.length === 1 ? missingGroupCount !== 0 : missingGroupCount < 1) {
    throw invalid();
  }

  const groups = [...head, ...Array<string>(missingGroupCount).fill('0'), ...tail];
  const bytes: number[] = [];

  for (const group of groups) {
    if (!/^[0-9a-fA-F]{1,4}$/.test(group)) {
      throw invalid();
    }
    const value = Number.parseInt(group, 16);
    bytes.push((value >> 8) & 0xff, value & 0xff);
  }

  return bytes;
}

/**
 * This validator rejects a non-empty value that is not an IPv6 address by {@link isIpv6Address}.
 * An empty value passes, so `Validators.required` owns that message. The error key is
 * `invalidIpv6`.
 */
export function ipv6AddressValidator(control: AbstractControl): ValidationErrors | null {
  const value: unknown = control.value;

  if (typeof value !== 'string' || value.length === 0) {
    return null;
  }

  return isIpv6Address(value) ? null : { invalidIpv6: true };
}
