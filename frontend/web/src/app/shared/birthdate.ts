// Birthdate rules mirrored from the API (BirthdateRules): 1900-01-01 .. today.
// The forms use mat-datepicker (Date values); the API takes yyyy-MM-dd strings.
export const BIRTHDATE_MIN = new Date(1900, 0, 1);

/** Start of today, local time (the max for a birthdate picker). */
export function birthdateMax(): Date {
  const d = new Date();
  return new Date(d.getFullYear(), d.getMonth(), d.getDate());
}

export function isValidBirthdate(value: Date | null | undefined): boolean {
  return value instanceof Date && !isNaN(value.getTime()) && value >= BIRTHDATE_MIN && value <= birthdateMax();
}

/** Date → yyyy-MM-dd (local date, no time-zone shift). */
export function toIsoDate(value: Date | null | undefined): string | null {
  if (!isValidDate(value)) return null;
  const d = value as Date;
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** yyyy-MM-dd → local Date (new Date('yyyy-MM-dd') would parse as UTC and can shift a day). */
export function fromIsoDate(value: string | null | undefined): Date | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value ?? '');
  return m ? new Date(+m[1], +m[2] - 1, +m[3]) : null;
}

function isValidDate(value: Date | null | undefined): boolean {
  return value instanceof Date && !isNaN(value.getTime());
}
