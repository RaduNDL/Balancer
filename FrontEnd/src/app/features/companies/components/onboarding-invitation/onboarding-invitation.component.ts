import { DatePipe } from '@angular/common';

import {
  Component,
  inject,
  input,
  signal
} from '@angular/core';

import {
  FormBuilder,
  ReactiveFormsModule,
  Validators
} from '@angular/forms';

import {
  AuthService
} from '../../../../core/services/auth.service';

import {
  OnboardingInvitation,
  OnboardingService
} from '../../../../core/services/onboarding.service';

@Component({
  selector: 'app-onboarding-invitation',
  standalone: true,
  imports: [ReactiveFormsModule, DatePipe],
  template: `
    <section class="company-card invitation-panel">
     <span class="eyebrow">Workspace invitation</span>

      <h2>Approve workspace access.</h2>

      <p>
      Select the roles you approve for this person.
      The invitee can choose only a role permitted by this invitation.
      </p>

      <form [formGroup]="form" (ngSubmit)="create()">
        <div class="form-group">
          <label for="invite-email">Account email</label>

          <input
            id="invite-email"
            type="email"
            formControlName="email"
            maxlength="254"
            required
          >
        </div>

        <fieldset class="intent-fieldset">
          <legend>Roles you approve</legend>

          <div class="invite-role-options">
            @for (role of roles; track role) {
              <label class="invite-role-option">
                <input
                  type="checkbox"
                  [formControlName]="role"
                >

                {{ role }}
              </label>
            }
          </div>
        </fieldset>

        @if (errorMessage()) {
          <p class="message error" role="alert">
            {{ errorMessage() }}
          </p>
        }

        <button
          class="button button-primary"
          type="submit"
          [disabled]="loading()"
        >
          {{ loading() ? 'Creating…' : 'Create invitation' }}
        </button>
      </form>

      @if (invitation(); as invitation) {
        <div class="next-step" aria-live="polite">
          <label for="generated-invite">Invitation code</label>

          <textarea
            id="generated-invite"
            [value]="invitation.code"
            rows="4"
            readonly
            spellcheck="false"
          ></textarea>

          <p class="field-help">
            Expires:
            {{ invitation.expiresAtUtc | date:'medium' }}.
            Share this code privately with the invited person.
            No email was sent.
          </p>

          <button
            class="button button-secondary"
            type="button"
            (click)="copy()"
          >
            {{ copied() ? 'Copied ✓' : 'Copy code' }}
          </button>
        </div>
      }
    </section>
  `
})
export class OnboardingInvitationComponent {
  // Componenta primește firma din pagina părinte.
  readonly companyId = input.required<number>();

  private readonly fb = inject(FormBuilder);
  private readonly onboarding = inject(OnboardingService);
  private readonly auth = inject(AuthService);

  readonly roles = [
    'Admin',
    'Accountant',
    'Manager',
    'Employee'
  ] as const;

  readonly loading = signal(false);
  readonly copied = signal(false);
  readonly errorMessage = signal('');

  readonly invitation =
    signal<OnboardingInvitation | null>(null);

  readonly form = this.fb.nonNullable.group({
    email: ['', [
      Validators.required,
      Validators.email,
      Validators.maxLength(254)
    ]],

    // Implicit permitem numai rolul cu accesul cel mai limitat.
    Admin: false,
    Accountant: false,
    Manager: false,
    Employee: true
  });

  async create(): Promise<void> {
    if (this.loading()) {
      return;
    }

    this.errorMessage.set('');
    this.invitation.set(null);
    this.copied.set(false);

    const values = this.form.getRawValue();

    const roles = this.roles.filter(
      role => values[role]
    );

    if (this.form.invalid || !roles.length) {
      this.errorMessage.set(
        'Enter a valid email and approve at least one role.'
      );
      return;
    }

    this.loading.set(true);

    try {
      this.invitation.set(
        await this.onboarding.createInvitation(
          this.companyId(),
          values.email.trim(),
          roles
        )
      );
    } catch (error) {
      this.errorMessage.set(
        this.auth.getErrorMessage(error)
      );
    } finally {
      this.loading.set(false);
    }
  }

  async copy(): Promise<void> {
    const invitation = this.invitation();

    if (!invitation) {
      return;
    }

    try {
      await navigator.clipboard.writeText(invitation.code);
      this.copied.set(true);
    } catch {
      this.errorMessage.set(
        'Select the invitation code and copy it manually.'
      );
    }
  }
}