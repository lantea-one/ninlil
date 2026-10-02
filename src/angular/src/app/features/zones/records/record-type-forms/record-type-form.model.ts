import { FormGroup } from '@angular/forms';

/**
 * This interface is the contract for every record-type data form in
 * {@link RECORD_TYPE_FORM_REGISTRY}. `RecordFormComponent` hosts these forms without knowing their
 * fields, so a new record type needs a new component and a registry entry, nothing else.
 *
 * A conforming component:
 * - Declares a signal `input()` named `initialData` of type `TData | null`: `null` for a new record,
 *   the stored data when editing. It is not on this interface, because an `input()` cannot be
 *   expressed as an interface member, but the name must match: `RecordFormComponent` passes it by
 *   name through `ngComponentOutletInputs`.
 * - Exposes {@link form}, built with `NonNullableFormBuilder` so an untouched control is never
 *   `null`. `RecordFormComponent` reads its `valid` flag to enable "Save".
 * - Exposes {@link getData}, which turns the form value into the `data` JSON the backend's
 *   `RecordDataValidator.EncodeDataOrThrow` expects for the type: camelCase names that match the
 *   backend record-data model field for field, e.g. `{ address: "203.0.113.5" }` for an `A`
 *   record. The API returns stored data in the same camelCase shape.
 *
 * `a-record-form.component.ts` is the simplest example to copy.
 *
 * `TData` defaults to `unknown` rather than `Record<string, unknown>`: a plain interface such as
 * each `XxxFormData` only satisfies an index-signature type if it declares one, and `unknown`
 * accepts every shape the registry maps.
 */
export interface RecordDataFormComponent<TData = unknown> {
  /** The record type's reactive form. */
  readonly form: FormGroup;

  /** This method returns {@link form}'s current value shaped for the request's `data` field. */
  getData(): TData;
}
