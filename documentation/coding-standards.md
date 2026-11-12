# Coding standards (review)

Judgement rules a reviewer applies to a diff. Mechanical rules belong in CI (`.github/workflows/ci.yml`), not here.

## Frontend (Angular)

- **Error UI reads the control.** A form's error markers, `<mat-error>` text and tooltips come from the control's own state (`ctrl.invalid`, `ctrl.touched`, `ctrl.hasError('…')`). Put the rule in a validator (`required`, `pattern`, `minlength`) and let the UI read it. A marker that re-checks the model value (`!name.trim()`) drifts from what blocks Save.
- **Errors wait for the user.** A blank form opens clean: error styling shows only after the field is touched or a save is attempted (Material's default error state).
- **Re-selecting the same record resets explicitly.** Setting a signal to its current value notifies nobody, so a "New" or "Refresh" that lands on the same record (often `null`) must reset the form directly.
