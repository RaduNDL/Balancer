import {
  Component,
  inject,
  signal
} from '@angular/core';

import {
  FormBuilder,
  ReactiveFormsModule,
  Validators
} from '@angular/forms';

import {
  ActivatedRoute,
  Router,
  RouterLink
} from '@angular/router';

import {
  AuthService
} from '../../../../core/services/auth.service';

import {
  OnboardingService
} from '../../../../core/services/onboarding.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink
  ],
  templateUrl: './login.component.html'
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly onboarding = inject(OnboardingService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  // Semnalele actualizează automat mesajele și butonul din HTML.
  readonly loading = signal(false);
  readonly errorMessage = signal('');

  // Este folosit când înregistrarea a reușit,
  // dar utilizatorul trebuie să se autentifice manual.
  readonly accountCreated =
    this.route.snapshot.queryParamMap.get('registered') === 'true';

  // nonNullable păstrează valorile câmpurilor de tip string.
  readonly form = this.fb.nonNullable.group({
    email: ['', [
      Validators.required,
      Validators.email,
      Validators.maxLength(254)
    ]],

    password: ['', [
      Validators.required,
      Validators.maxLength(128)
    ]]
  });

  async submit(): Promise<void> {
    // Prevenim trimiterea simultană a mai multor cereri.
    if (this.loading()) {
      return;
    }

    this.errorMessage.set('');

    if (this.form.invalid) {
      this.form.markAllAsTouched();

      this.errorMessage.set(
        'Enter a valid email and your password.'
      );

      return;
    }

    this.loading.set(true);

    try {
      const values = this.form.getRawValue();

      // Backendul verifică parola și emite cookie-ul.
      // Autentificarea nu atribuie automat rolul Owner.
      await this.auth.login({
        email: values.email.trim(),
        password: values.password
      });

      // Eliminăm parola din formular după autentificare.
      this.form.controls.password.reset();

      // Verificăm în backend dacă acest cont a terminat configurarea.
      const status = await this.onboarding.getStatus();

      // Conturile configurate intră în dashboard.
      // Conturile noi continuă cu chestionarul.
      await this.router.navigateByUrl(
        status.completed
          ? '/dashboard'
          : '/onboarding'
      );
    } catch (error) {
      this.errorMessage.set(
        this.auth.getErrorMessage(error)
      );
    } finally {
      // Reactivăm formularul și după o eroare.
      this.loading.set(false);
    }
  }
}