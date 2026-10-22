import {
  Component,
  inject,
  OnInit,
  signal
} from '@angular/core';

import { RouterLink } from '@angular/router';

import {
  CompanyMembership
} from '../../../../core/models/auth.models';

import {
  AuthService
} from '../../../../core/services/auth.service';

import {
  OnboardingService
} from '../../../../core/services/onboarding.service';

@Component({
  selector: 'app-onboarding-success',
  standalone: true,
  imports: [RouterLink],
  template: `
    <main class="dashboard celebration-page">
      @if (loading()) {
        <p role="status">Loading your workspace…</p>
      }

      @if (errorMessage()) {
        <section class="company-card">
          <p class="message error" role="alert">
            {{ errorMessage() }}
          </p>

          <a class="button button-primary" routerLink="/dashboard">
            My workspaces
          </a>
        </section>
      }

      @if (membership(); as member) {
        <section class="celebration-card">
          <!-- Particule decorative; nu interceptează interacțiunile. -->
          <div class="confetti" aria-hidden="true">
            @for (particle of particles; track particle) {
              <span
                class="confetti-piece"
                [style.--i]="particle"
              ></span>
            }
          </div>

          <span class="celebration-check" aria-hidden="true">✓</span>

          <span class="eyebrow">
            Your next chapter starts here
          </span>

          <h1>
            Congratulations,<br>
            <em>{{ auth.currentUser()?.fullName }}.</em>
          </h1>

          <p>
            You are now part of
            <strong>{{ member.companyName }}</strong>.
          </p>

          <p>Your company role:</p>

          <span
            class="role-badge"
            [attr.data-role]="member.role"
          >
            {{ member.role }}
          </span>

          <p class="muted">
            Your role has been saved. Your workspace is ready.
          </p>

          <a
            class="button button-primary"
            [routerLink]="['/companies', member.companyId]"
          >
            Open my workspace ↗
          </a>
        </section>
      }
    </main>
  `
})
export class OnboardingSuccessComponent implements OnInit {
  readonly auth = inject(AuthService);

  private readonly onboarding = inject(OnboardingService);

  readonly loading = signal(true);
  readonly errorMessage = signal('');

  readonly membership =
    signal<CompanyMembership | null>(null);

  readonly particles = Array.from(
    { length: 12 },
    (_, index) => index
  );

  async ngOnInit(): Promise<void> {
    try {
      // Recitim rolul real din backend, inclusiv după refresh.
      // Nu îl luăm din URL sau dintr-o valoare inventată în browser.
      this.membership.set(
        await this.onboarding.getResult()
      );
    } catch (error) {
      this.errorMessage.set(
        this.auth.getErrorMessage(error)
      );
    } finally {
      this.loading.set(false);
    }
  }
}