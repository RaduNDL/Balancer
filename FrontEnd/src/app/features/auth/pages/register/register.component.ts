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
  Router,
  RouterLink
} from '@angular/router';

import {
  AuthService
} from '../../../../core/services/auth.service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink
  ],
  templateUrl: './register.component.html'
})
export class RegisterComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly loading = signal(false);
  readonly errorMessage = signal('');

  // Înregistrarea creează contul personal.
  // Firma și rolul sunt stabilite ulterior prin onboarding.
  readonly form = this.fb.nonNullable.group({
    fullName: ['', [
      Validators.required,
      Validators.minLength(2),
      Validators.maxLength(150)
    ]],

    email: ['', [
      Validators.required,
      Validators.email,
      Validators.maxLength(254)
    ]],

    password: ['', [
      Validators.required,
      Validators.minLength(12),
      Validators.maxLength(128)
    ]],

    confirmPassword: ['', [
      Validators.required,
      Validators.maxLength(128)
    ]]
  });

  async submit(): Promise<void> {
    if (this.loading()) {
      return;
    }

    this.errorMessage.set('');

    if (this.form.invalid) {
      this.form.markAllAsTouched();

      this.errorMessage.set(
        'Complete all fields. Use a password of 12 to 128 characters.'
      );

      return;
    }

    const values = this.form.getRawValue();

    // Eliminăm spațiile exterioare din nume și email.
    // Parola rămâne exact așa cum a fost introdusă.
    const fullName = values.fullName.trim();
    const email = values.email.trim();

    if (fullName.length < 2) {
      this.errorMessage.set(
        'Enter a valid full name.'
      );

      return;
    }

    if (values.password !== values.confirmPassword) {
      this.errorMessage.set(
        'Passwords do not match.'
      );

      return;
    }

    this.loading.set(true);

    try {
      // Acest apel creează numai utilizatorul în baza de date.
      await this.auth.register({
        fullName,
        email,
        password: values.password,
        confirmPassword: values.confirmPassword
      });

      // Contul există deja din acest moment.
      // Eliminăm datele introduse din formular.
      this.form.reset();

      try {
        // Obținem cookie-ul pentru a continua direct cu chestionarul.
        await this.auth.login({
          email,
          password: values.password
        });
      } catch {
        // Dacă autentificarea automată eșuează, utilizatorul
        // poate intra manual în contul care tocmai a fost creat.
        await this.router.navigate(['/auth/login'], {
          queryParams: {
            registered: 'true'
          }
        });

        return;
      }

      // Nu deschidem dashboardul imediat după înregistrare.
      // Primul pas pentru contul nou este chestionarul.
      await this.router.navigateByUrl('/onboarding');
    } catch (error) {
      this.errorMessage.set(
        this.auth.getErrorMessage(error)
      );
    } finally {
      this.loading.set(false);
    }
  }
}