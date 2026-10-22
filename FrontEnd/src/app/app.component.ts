import {
  Component,
  computed,
  effect,
  inject,
  OnInit,
  signal,
  untracked
} from '@angular/core';

import { HttpErrorResponse } from '@angular/common/http';

import {
  Router,
  RouterLink,
  RouterLinkActive,
  RouterOutlet
} from '@angular/router';

import { AuthService } from './core/services/auth.service';
import {
  MessagingService
} from './core/services/messaging.service';
@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive
  ],
  templateUrl: './app.component.html'
})
export class AppComponent implements OnInit {
  readonly auth = inject(AuthService);
readonly chat = inject(MessagingService);

constructor() {
  // Conexiunea urmărește contul autentificat.
  // La logout sunt eliminate conexiunea și lista conversațiilor.
  effect(() => {
    const userId = this.auth.currentUser()?.id ?? null;

    untracked(() => {
      this.chat.useUser(userId);
    });
  });
}
  private readonly router = inject(Router);

  readonly menuOpen = signal(false);
  readonly signingOut = signal(false);
  readonly checkingSession = signal(true);
  readonly navError = signal('');

  // Contul fără apartenențe este afișat ca un cont personal.
  // Aceasta este o stare a contului, nu un rol de firmă.
  readonly isPersonalAccount = computed(() => {
    const user = this.auth.currentUser();

    return user !== null && user.memberships.length === 0;
  });

  // Inițialele sunt calculate automat când se schimbă utilizatorul.
  readonly initials = computed(() => {
    const name = this.auth.currentUser()?.fullName ?? 'B';

    return name
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map(part => part[0])
      .join('')
      .toUpperCase();
  });

  async ngOnInit(): Promise<void> {
    // Paginile publice nu folosesc authGuard.
    // Verificăm separat cookie-ul pentru a reface navbarul la refresh.
    try {
      await this.auth.loadCurrentUser();
    } catch (error) {
      // Un vizitator fără cookie valid primește normal 401.
      // Paginile publice rămân accesibile.
      if (
        !(error instanceof HttpErrorResponse)
        || error.status !== 401
      ) {
        this.navError.set(
          'We could not check your session. Public pages are still available.'
        );
      }
    } finally {
      this.checkingSession.set(false);
    }
  }

  async logout(): Promise<void> {
    if (this.signingOut()) {
      return;
    }

    this.signingOut.set(true);
    this.navError.set('');

    try {
      await this.auth.logout();

      this.menuOpen.set(false);

      // După deconectare revenim la pagina publică.
      await this.router.navigateByUrl('/');
    } catch (error) {
      this.navError.set(
        this.auth.getErrorMessage(error)
      );
    } finally {
      this.signingOut.set(false);
    }
  }
}