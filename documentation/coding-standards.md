# Coding standards

Judgement calls a reviewer checks on every diff. Rules a tool can check belong in CI
(`.github/workflows/ci.yml`), not here.

## Shared look across pages

- **Icon-and-text rows** (a `mat-icon` followed by text, such as a schedule, venue or link) use the
  shared global classes in `frontend/web/src/styles.scss`: `.ev-line` inside cards, `.detail-row`
  on detail pages. A page that needs its own row class keeps the same spacing: an 8px gap and an
  18px icon. A new page-local gap or icon size is a finding.
- **Event schedules** render through `<app-schedule [start] [end]>` on the web and
  `ScheduleText` in the API. A new date format for an event's start or end is a finding.

## Forms

- **Error UI reads the control.** A form's error markers, `<mat-error>` text and tooltips come from
  the control's own state (`ctrl.invalid`, `ctrl.touched`, `ctrl.hasError('…')`). Put the rule in a
  validator (`required`, `pattern`, `minlength`) and let the UI read it. A marker that re-checks the
  model value (`!name.trim()`) drifts from what blocks Save.
- **Errors wait for the user.** A blank form opens clean: error styling shows only after the field
  is touched or a save is attempted (Material's default error state).
- **Re-selecting the same record resets explicitly.** Setting a signal to its current value
  notifies nobody, so a "New" or "Refresh" that lands on the same record (often `null`) must reset
  the form directly.
