# Display photo and face-recognition photo

Two pictures, stored and used separately:

| Picture | Stored in | Used by |
|---------|-----------|---------|
| Uploaded display photo | `User.PhotoUrl` (account) / `Registration.GuestPhoto` (guest) | Welcome screen, check-in result, pass, TV — or the initials avatar when there is none |
| Face-recognition capture | `User.FacePhoto` / `Registration.FacePhoto`, beside `FaceDescriptor` | The face engine's gallery and recognition only — never shown to anyone |

## Behaviour

- Uploading or changing the profile / display picture writes only `PhotoUrl` / `GuestPhoto`.
- Registering or re-capturing a face writes only `FaceDescriptor` + `FacePhoto`. It never
  replaces the uploaded picture, and removing the face clears both face fields while keeping the
  picture.
- The face gallery (`GET api/face-engine/events/{id}/gallery`) sends `FacePhoto ?? PhotoUrl` for
  an account and `FacePhoto ?? GuestPhoto` for a guest. The fallback keeps older records (which
  have no separate capture) in recognition.
- QR, barcode, Auto and Face check-in / verify-only results use the API's display photo
  (`Registration.DisplayPhoto` → `CheckInResult.PhotoUrl`). The TV waits for that result and
  never uses the engine's recognition image as the fallback; with no display photo it shows the
  avatar/initials state.
- Offline registration queues the display picture and the face capture separately
  (`displayPicture`, `facePhoto`), and a refreshed offline bundle carries each guest's display
  photo (`OfflineRegDto.DisplayPicture`) so offline check-in shows the right picture.
- A face already registered to another person is rejected (account, admin and guest flows).

## Deploying this change

The migration `SeparateRecognitionAndDisplayPhotos` adds two nullable columns
(`Users.FacePhoto`, `Registrations.FacePhoto`); it does not rewrite existing photos. Deploy the
API and web app together, restart the API, then refresh open TVs and download a fresh offline
bundle. An upload that an older build already overwrote with the face still cannot be recovered —
the person needs to upload their display photo again.
