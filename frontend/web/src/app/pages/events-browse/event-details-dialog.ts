import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { EventDto } from '../../core/models';
import { CATEGORY_CONFIRMED_STATE, CategoryWarningService } from '../../core/category-warning.service';
import { openImageLightbox } from '../../shared/image-lightbox';
import { ScheduleComponent } from '../../shared/schedule';
import { canRegister } from './event-tab';

export interface EventDetailsData {
  event: EventDto;
  registered: boolean;
  // True when re-registration is a full duplicate (single event already registered).
  blocked: boolean;
}

@Component({
  selector: 'app-event-details-dialog',
  imports: [ScheduleComponent, MatDialogModule, MatButtonModule, MatIconModule],
  templateUrl: './event-details-dialog.html',
  styleUrl: './event-details-dialog.scss',
})
export class EventDetailsDialogComponent {
  private data = inject<EventDetailsData>(MAT_DIALOG_DATA);
  readonly e = this.data.event;
  readonly registered = this.data.registered;
  readonly blocked = this.data.blocked;
  private ref = inject(MatDialogRef<EventDetailsDialogComponent>);
  private router = inject(Router);
  private dialog = inject(MatDialog);
  private categoryWarning = inject(CategoryWarningService);

  viewImage(src: string | null | undefined): void {
    openImageLightbox(this.dialog, src);
  }

  canRegister(): boolean {
    return canRegister(this.e);
  }

  close(): void {
    this.ref.close();
  }

  // Like the card's Register: warn about a team-category mismatch first; Cancel keeps the dialog open.
  async register(): Promise<void> {
    const warn = this.canRegister() && !this.registered;
    if (warn && !(await this.categoryWarning.confirm(this.e.category))) return;
    this.ref.close();
    this.router.navigate(['/e', this.e.publicSlug], warn ? { state: { [CATEGORY_CONFIRMED_STATE]: true } } : {});
  }

  goMyEvents(): void {
    this.ref.close();
    this.router.navigate(['/my-events']);
  }
}
