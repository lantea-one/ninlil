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
