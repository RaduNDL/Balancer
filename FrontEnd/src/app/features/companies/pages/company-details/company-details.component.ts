import {
  OnboardingInvitationComponent
} from '../../components/onboarding-invitation/onboarding-invitation.component';
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
import { ActivatedRoute, RouterLink } from '@angular/router';

import { CompanyMembership } from '../../../../core/models/auth.models';
import { AuthService } from '../../../../core/services/auth.service';
import {
  AssignableCompanyRole,
  CompanyMember,
  CompanyService
} from '../../../../core/services/company.service';

@Component({
  selector: 'app-company-details',
  standalone: true,
 imports: [
  ReactiveFormsModule,
  RouterLink,
  OnboardingInvitationComponent
],
  templateUrl: './company-details.component.html'
})
export class CompanyDetailsComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly formBuilder = inject(FormBuilder);
  private readonly companyService = inject(CompanyService);
  private readonly auth = inject(AuthService);

  readonly company = signal<CompanyMembership | null>(null);
  readonly members = signal<CompanyMember[]>([]);

  readonly loading = signal(true);
  readonly addingMember = signal(false);

  readonly errorMessage = signal('');
  readonly successMessage = signal('');

  // Opțiunile formularului. Owner nu este o opțiune.
  readonly availableRoles: readonly AssignableCompanyRole[] = [
    'Admin',
    'Accountant',
    'Manager',
    'Employee'
  ];

  // computed recalculează valoarea când se schimbă firma.
  readonly canViewMembers = computed(() => {
    const role = this.company()?.role;

    return role === 'Owner' || role === 'Admin';
  });

  readonly canAddMembers = computed(
    () => this.company()?.role === 'Owner'
  );

  readonly memberForm = this.formBuilder.nonNullable.group({
    email: ['', [
      Validators.required,
      Validators.email,
      Validators.maxLength(254)
    ]],
    role: this.formBuilder.nonNullable.control<AssignableCompanyRole>(
      'Employee',
      Validators.required
    )
  });

  // Angular apelează această metodă la inițializarea componentei.
  ngOnInit(): void {
    void this.loadCompany();
  }

  private async loadCompany(): Promise<void> {
    const idValue = this.route.snapshot.paramMap.get('companyId');
    const companyId = Number(idValue);

    if (!Number.isSafeInteger(companyId) || companyId <= 0) {
      this.errorMessage.set('Invalid company address.');
      this.loading.set(false);
      return;
    }

    try {
      // Backendul verifică apartenența înainte de a returna firma.
      const company = await this.companyService.getCompany(companyId);

      this.company.set(company);

      // Nu cerem lista pentru rolurile care nu au acces la ea.
      if (this.canViewMembers()) {
        this.members.set(
          await this.companyService.getMembers(companyId)
        );
      }
    } catch (error) {
      this.errorMessage.set(this.auth.getErrorMessage(error));
    } finally {
      this.loading.set(false);
    }
  }

  async addMember(): Promise<void> {
    const company = this.company();

    if (!company || !this.canAddMembers() || this.addingMember()) {
      return;
    }

    this.errorMessage.set('');
    this.successMessage.set('');

    if (this.memberForm.invalid) {
      this.memberForm.markAllAsTouched();
      this.errorMessage.set('Enter a valid email and select a role.');
      return;
    }

    this.addingMember.set(true);

    try {
      const values = this.memberForm.getRawValue();

      const member = await this.companyService.addMember(
        company.companyId,
        values.email.trim(),
        values.role
      );

      // Afișăm membrul confirmat de backend.
      this.members.update(members =>
        [...members, member].sort((first, second) =>
          first.fullName.localeCompare(second.fullName)
        )
      );

      this.memberForm.reset({
        email: '',
        role: 'Employee'
      });

      this.successMessage.set(
        `${member.fullName} was added as ${member.role}.`
      );
    } catch (error) {
      this.errorMessage.set(this.auth.getErrorMessage(error));
    } finally {
      this.addingMember.set(false);
    }
  }
}