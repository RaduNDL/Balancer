import { DatePipe } from '@angular/common';

import {
  Component,
  computed,
  inject,
  OnInit,
  signal
} from '@angular/core';

import {
  FormBuilder,
  ReactiveFormsModule,
  Validators
} from '@angular/forms';

import { Router, RouterLink } from '@angular/router';

import {
  CompanyRole
} from '../../../../core/models/auth.models';

import {
  AuthService
} from '../../../../core/services/auth.service';

import {
  CompanyIntent,
  InvitationPreview,
  OnboardingService,
  WorkFocus
} from '../../../../core/services/onboarding.service';

@Component({
  selector: 'app-profile-setup',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, DatePipe],
  templateUrl: './profile-setup.component.html'
})
export class ProfileSetupComponent implements OnInit {
  readonly auth = inject(AuthService);

  private readonly fb = inject(FormBuilder);
  private readonly onboarding = inject(OnboardingService);
  private readonly router = inject(Router);

  readonly checking = signal(true);
  readonly ready = signal(false);
  readonly alreadyMember = signal(false);
  readonly loading = signal(false);
  readonly verifyingInvitation = signal(false);
  readonly errorMessage = signal('');

  readonly invitation =
    signal<InvitationPreview | null>(null);

  // Reținem exact codul pentru care am primit verificarea.
  private verifiedCode = '';

  // Activitățile și rolurile corespunzătoare din interfață.
  // Backendul are propria verificare independentă.
  readonly workOptions: {
    value: WorkFocus;
    role: Exclude<CompanyRole, 'Owner'>;
    title: string;
    description: string;
  }[] = [
    {
      value: 'workspace_administration',
      role: 'Admin',
      title: 'Workspace administration',
      description:
        'Manage workspace settings, user access and internal administration.'
    },
    {
      value: 'accounting',
      role: 'Accountant',
      title: 'Finance and accounting',
      description:
        'Work with invoices, expenses, payments and financial records.'
    },
    {
      value: 'project_coordination',
      role: 'Manager',
      title: 'Projects and team coordination',
      description:
        'Plan delivery, assign work and coordinate project deadlines.'
    },
    {
      value: 'assigned_work',
      role: 'Employee',
      title: 'My assigned work',
      description:
        'Complete assigned tasks and use resources shared with me.'
    }
  ];

  // Afișăm numai opțiunile aprobate prin invitație.
  readonly availableWorkOptions = computed(() => {
    const allowed = this.invitation()?.allowedRoles ?? [];

    return this.workOptions.filter(
      option => allowed.includes(option.role)
    );
  });

  readonly form = this.fb.nonNullable.group({
    companyIntent:
      this.fb.nonNullable.control<CompanyIntent>('unsure'),

    companyName: ['', Validators.maxLength(200)],

    invitationCode: ['', Validators.maxLength(4096)],

    confirmOwnership: false,

    acceptInvitation: false,

    workFocus:
      this.fb.nonNullable.control<WorkFocus>('unsure')
  });

  ngOnInit(): void {
    void this.loadStatus();
  }

  // Verificăm dacă utilizatorul a finalizat deja configurarea.
  // Metoda poate fi apelată din nou după o eroare de conexiune.
  async loadStatus(): Promise<void> {
    this.checking.set(true);
    this.ready.set(false);
    this.errorMessage.set('');

    try {
      const status = await this.onboarding.getStatus();

      this.alreadyMember.set(status.completed);

      if (status.hasResult) {
        await this.router.navigateByUrl('/onboarding/success');
      } else {
        this.ready.set(true);
      }
    } catch (error) {
      this.errorMessage.set(
        this.auth.getErrorMessage(error)
      );
    } finally {
      this.checking.set(false);
    }
  }

  // Schimbarea traseului anulează confirmările precedente.
  changeIntent(): void {
    this.clearInvitation();
    this.form.controls.confirmOwnership.setValue(false);
  }

  // Un cod modificat trebuie verificat din nou.
  clearInvitation(): void {
    this.invitation.set(null);
    this.verifiedCode = '';

    this.form.controls.acceptInvitation.setValue(false);
    this.form.controls.workFocus.setValue('unsure');

    this.errorMessage.set('');
  }

  async validateInvitation(): Promise<void> {
    if (this.verifyingInvitation() || this.loading()) {
      return;
    }

    const code =
      this.form.controls.invitationCode.value.trim();

    this.clearInvitation();

    if (!code || code.length > 4096) {
      this.errorMessage.set('Paste a valid invitation code.');
      return;
    }

    this.verifyingInvitation.set(true);

    try {
      const preview =
        await this.onboarding.previewInvitation(code);

      // Ignorăm răspunsul pentru un cod schimbat între timp.
      if (
        this.form.controls.invitationCode.value.trim() !== code
        || this.form.controls.companyIntent.value !== 'join_workspace'
      ) {
        return;
      }

      this.verifiedCode = code;
      this.invitation.set(preview);

      const options = this.availableWorkOptions();

      // Dacă Owner a aprobat un singur rol, îl afișăm direct.
      // Utilizatorul trebuie totuși să accepte invitația.
      if (options.length === 1) {
        this.form.controls.workFocus.setValue(
          options[0].value
        );
      }
    } catch (error) {
      if (
        this.form.controls.invitationCode.value.trim() === code
        && this.form.controls.companyIntent.value === 'join_workspace'
      ) {
        this.errorMessage.set(
          this.auth.getErrorMessage(error)
        );
      }
    } finally {
      this.verifyingInvitation.set(false);
    }
  }

  // Calculează rezumatul vizual.
  // Rolul salvat este stabilit separat în backend.
  selectedRole(): CompanyRole | null {
    if (
      this.form.controls.companyIntent.value === 'create_workspace'
    ) {
      return 'Owner';
    }

    return this.availableWorkOptions().find(
      option =>
        option.value === this.form.controls.workFocus.value
    )?.role ?? null;
  }

  async submit(): Promise<void> {
    if (
      this.loading()
      || this.verifyingInvitation()
      || this.checking()
      || !this.ready()
      || this.alreadyMember()
    ) {
      return;
    }

    this.errorMessage.set('');

    const values = this.form.getRawValue();

    if (values.companyIntent === 'unsure') {
      this.errorMessage.set(
        'Choose whether to create a workspace or join a team.'
      );
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();

      this.errorMessage.set(
        'Check the length of the company name or invitation code.'
      );
      return;
    }

    if (values.companyIntent === 'create_workspace') {
      if (
        values.companyName.trim().length < 2
        || !values.confirmOwnership
      ) {
        this.errorMessage.set(
          'Enter a company name and confirm responsibility for the new workspace.'
        );
        return;
      }
    } else if (
      !this.invitation()
      || values.invitationCode.trim() !== this.verifiedCode
      || !this.selectedRole()
      || !values.acceptInvitation
    ) {
      this.errorMessage.set(
        'Verify your invitation, choose your responsibility and confirm that you want to join.'
      );
      return;
    }

    this.loading.set(true);

    try {
      const response = await this.onboarding.complete({
        companyIntent: values.companyIntent,

        companyName:
          values.companyIntent === 'create_workspace'
            ? values.companyName.trim()
            : '',

        invitationCode:
          values.companyIntent === 'join_workspace'
            ? values.invitationCode.trim()
            : '',

        confirmOwnership: values.confirmOwnership,
        acceptInvitation: values.acceptInvitation,
        workFocus: values.workFocus
      });

      if (
        response.status !== 'Completed'
        || !response.membership
      ) {
        this.errorMessage.set(response.message);
        return;
      }

      const membership = response.membership;

      // Actualizăm profilul cu apartenența confirmată de server.
      this.auth.currentUser.update(user => user
        ? {
            ...user,
            memberships: [
              ...user.memberships.filter(
                item =>
                  item.companyId !== membership.companyId
              ),
              membership
            ]
          }
        : user
      );

      await this.router.navigateByUrl('/onboarding/success');
    } catch (error) {
      this.errorMessage.set(
        this.auth.getErrorMessage(error)
      );
    } finally {
      this.loading.set(false);
    }
  }
}