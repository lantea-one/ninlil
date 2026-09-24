import { FormGroup } from '@angular/forms';

/**
 * This interface is the contract every record-type-specific data-entry component in
 * {@link RECORD_TYPE_FORM_REGISTRY} implements -- the single most important architectural
 * decision behind the record-editing UI, since it lets the registry grow to cover every record
 * type the backend supports without ever touching {@link RecordFormComponent} or this contract
 * again.
 *
 * A conforming component:
 * - Accepts a signal `input()` named `initialData` of type `TData | null` (`null` for a brand new
 *   record being created, a real value when editing an existing one) -- not declared on this
 *   interface itself, since TypeScript's structural typing cannot express an `input()`'s own
 *   compile-time-generated property shape as an interface member; every implementation must still
 *   declare it under that exact name, since {@link RecordFormComponent} supplies it by name via
 *   `NgComponentOutlet`'s own `ngComponentOutletInputs` map.
 * - Exposes {@link form}, a `FormGroup` built with `NonNullableFormBuilder` (so an untouched
 *   control never surfaces as `null`), which {@link RecordFormComponent} reads `.valid` from to
 *   decide whether "Save" is enabled, without needing to know anything about that type's own
 *   individual fields.
 * - Exposes {@link getData}, which reads {@link form}'s current value and shapes it into exactly
 *   the JSON structure `RecordsController`'s own `TryEncodeData` expects for that record type --
 *   camelCase field names matching the type's wire-format model 1:1 (e.g. an `A` record's
 *   `{ address: "203.0.113.5" }`), per this project's own confirmed REST-boundary camelCase
 *   convention (stored record data is always PascalCase; the REST API converts it).
 *
 * See `a-record-form.component.ts` for the reference shape a new form component should copy.
 *
 * `TData` deliberately defaults to `unknown`, not a `Record<string, unknown>` index-signature
 * type: TypeScript only allows a plain interface (every per-type `XxxFormData` shape here) to
 * satisfy an index-signature type when it structurally declares one itself, which none of these
 * intentionally narrow, field-specific interfaces do -- `unknown` accepts any concrete shape
 * covariantly instead, which is exactly what {@link RECORD_TYPE_FORM_REGISTRY}'s own map of
 * genuinely different `TData` shapes needs.
 */
export interface RecordDataFormComponent<TData = unknown> {
  /** This record type's own reactive form. */
  readonly form: FormGroup;

  /** This method reads {@link form}'s current value, shaped for the REST API's own `data` field. */
  getData(): TData;
}
