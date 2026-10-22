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
import { Router } from '@angular/router';

import { AuthService } from '../../../../core/services/auth.service';
import { PeopleService } from '../../../../core/services/people.service';
import { MessagingService } from '../../../../core/services/messaging.service';
import {
  InvitationPreview,
  OnboardingInvitation,
  OnboardingService
} from '../../../../core/services/onboarding.service';
import {
  PersonProfile
} from '../../../../core/models/people.models';

@Component({
  selector: 'app-people',
  standalone: true,
  imports: [ReactiveFormsModule, DatePipe],
  templateUrl: './people.component.html',
  styleUrl: './people.component.css'
})
export class PeopleComponent implements OnInit {
  readonly auth = inject(AuthService);

  private readonly people = inject(PeopleService);
  private readonly chat = inject(MessagingService);
  private readonly onboarding = inject(OnboardingService);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  readonly items = signal<PersonProfile[]>([]);
  readonly selected = signal<PersonProfile | null>(null);

  readonly page = signal(1);
  readonly total = signal(0);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly contacting = signal(false);
  readonly inviting = signal(false);
  readonly joining = signal(false);
  readonly verifying = signal(false);

  readonly error = signal('');
  readonly notice = signal('');
  readonly profileReady = signal(false);

  readonly inviteResult =
    signal<OnboardingInvitation | null>(null);

  readonly joinPreview =
    signal<InvitationPreview | null>(null);

  // Numai firmele în care utilizatorul este Owner apar în formular.
  readonly ownerCompanies = computed(() =>
    (this.auth.currentUser()?.memberships ?? [])
      .filter(m => m.role === 'Owner')
  );

  readonly roles = [
    'Admin',
    'Accountant',
    'Manager',
    'Employee'
  ] as const;

  // Versiunile împiedică răspunsurile vechi să suprascrie selecțiile noi.
  private listVersion = 0;
  private profileVersion = 0;
  private verifiedCode = '';

  readonly searchForm = this.fb.nonNullable.group({
    search: ['', Validators.maxLength(80)]
  });

  readonly profileForm = this.fb.nonNullable.group({
    fullName: ['', [
      Validators.required,
      Validators.minLength(2),
      Validators.maxLength(150)
    ]],
    jobTitle: ['', Validators.maxLength(100)],
    bio: ['', Validators.maxLength(1000)],
    skills: ['', Validators.maxLength(200)],
    isDiscoverable: true,
    showEmail: false
  });

  readonly inviteForm = this.fb.nonNullable.group({
    companyId: '',
    Admin: false,
    Accountant: false,
    Manager: false,
    Employee: true
  });

  readonly joinForm = this.fb.nonNullable.group({
    code: ['', [
      Validators.required,
      Validators.maxLength(4096)
    ]],
    role: '',
    accepted: false
  });

  ngOnInit(): void {
    void this.loadPeople();
    void this.loadOwnProfile();
  }

  // Încarcă rezultatele căutării.
  async loadPeople(page = 1): Promise<void> {
    if (this.searchForm.invalid) return;

    const version = ++this.listVersion;

    this.loading.set(true);
    this.error.set('');

    try {
      const result = await this.people.list(
        this.searchForm.controls.search.value.trim(),
        page
      );

      if (version !== this.listVersion) return;

      this.items.set(result.items);
      this.page.set(result.page);
      this.total.set(result.total);
    } catch (error) {
      if (version === this.listVersion) {
        this.error.set(this.auth.getErrorMessage(error));
      }
    } finally {
      if (version === this.listVersion) {
        this.loading.set(false);
      }
    }
  }

  async loadOwnProfile(): Promise<void> {
    try {
      this.profileForm.setValue(await this.people.me());
      this.profileReady.set(true);
    } catch (error) {
      this.error.set(this.auth.getErrorMessage(error));
    }
  }

  async saveProfile(): Promise<void> {
    if (this.saving() || !this.profileReady()) return;

    if (this.profileForm.invalid) {
      this.profileForm.markAllAsTouched();
      this.error.set('Check your profile fields.');
      return;
    }

    this.saving.set(true);
    this.error.set('');

    try {
      const data = this.profileForm.getRawValue();

      await this.people.save(data);

      // Actualizăm și numele din navbar.
      this.auth.currentUser.update(user => user
        ? { ...user, fullName: data.fullName.trim() }
        : user
      );

      ++this.profileVersion;
      this.selected.set(null);
      this.notice.set('Your profile was saved.');

      await this.loadPeople(1);
    } catch (error) {
      this.error.set(this.auth.getErrorMessage(error));
    } finally {
      this.saving.set(false);
    }
  }

  // Recitim profilul pentru a respecta setările sale actuale.
  async open(person: PersonProfile): Promise<void> {
    const version = ++this.profileVersion;

    this.selected.set(null);
    this.inviteResult.set(null);
    this.error.set('');
    this.notice.set('');

    this.inviteForm.controls.companyId.setValue(
      String(this.ownerCompanies()[0]?.companyId ?? '')
    );

    try {
      const profile = await this.people.get(person.id);

      if (version === this.profileVersion) {
        this.selected.set(profile);
      }
    } catch (error) {
      if (version === this.profileVersion) {
        this.error.set(this.auth.getErrorMessage(error));
      }
    }
  }

  initials(name: string): string {
    return name.trim()
      .split(/\s+/)
      .slice(0, 2)
      .map(part => part[0])
      .join('')
      .toUpperCase();
  }

  skills(person: PersonProfile): string[] {
    return person.skills
      .split(',')
      .map(skill => skill.trim())
      .filter(Boolean);
  }

  // Deschide cererea sau conversația deja existentă.
  async contact(): Promise<void> {
    const person = this.selected();

    if (!person || this.contacting()) return;

    this.contacting.set(true);
    this.error.set('');

    try {
      const id = await this.chat.requestUser(person.id);

      await this.router.navigate(['/messages'], {
        queryParams: { conversation: id }
      });
    } catch (error) {
      this.error.set(this.auth.getErrorMessage(error));
    } finally {
      this.contacting.set(false);
    }
  }

  async invite(): Promise<void> {
    const person = this.selected();

    if (!person || this.inviting()) return;

    const values = this.inviteForm.getRawValue();
    const companyId = Number(values.companyId);

    const roles = this.roles.filter(
      role => values[role]
    );

    if (!companyId || !roles.length) {
      this.error.set(
        'Select your workspace and at least one role.'
      );
      return;
    }

    this.inviting.set(true);
    this.inviteResult.set(null);
    this.error.set('');

    try {
      const result = await this.people.invite(
        person.id,
        companyId,
        roles
      );

      if (this.selected()?.id === person.id) {
        this.inviteResult.set(result);
      }
    } catch (error) {
      this.error.set(this.auth.getErrorMessage(error));
    } finally {
      this.inviting.set(false);
    }
  }

  async copy(): Promise<void> {
    const code = this.inviteResult()?.code;

    if (!code) return;

    try {
      await navigator.clipboard.writeText(code);
      this.notice.set(
        'Invitation code copied. Share it privately.'
      );
    } catch {
      this.error.set(
        'Select the code and copy it manually.'
      );
    }
  }

  // Editarea codului anulează verificarea precedentă.
  clearJoin(): void {
    this.joinPreview.set(null);
    this.verifiedCode = '';
    this.joinForm.controls.role.setValue('');
    this.joinForm.controls.accepted.setValue(false);
  }

  async verifyJoin(): Promise<void> {
    if (
      this.verifying()
      || this.joining()
      || this.joinForm.controls.code.invalid
    ) {
      return;
    }

    const code = this.joinForm.controls.code.value.trim();

    this.clearJoin();
    this.verifying.set(true);
    this.error.set('');

    try {
      const preview =
        await this.onboarding.previewInvitation(code);

      if (
        this.joinForm.controls.code.value.trim() !== code
      ) {
        return;
      }

      this.verifiedCode = code;
      this.joinPreview.set(preview);

      if (preview.allowedRoles.length === 1) {
        this.joinForm.controls.role.setValue(
          preview.allowedRoles[0]
        );
      }
    } catch (error) {
      this.error.set(this.auth.getErrorMessage(error));
    } finally {
      this.verifying.set(false);
    }
  }

  async acceptJoin(): Promise<void> {
    if (this.joining() || this.verifying()) return;

    const values = this.joinForm.getRawValue();
    const preview = this.joinPreview();

    if (
      !preview
      || !values.accepted
      || values.code.trim() !== this.verifiedCode
      || !preview.allowedRoles.some(
        role => role === values.role
      )
    ) {
      this.error.set(
        'Verify the invitation, select an approved role and confirm acceptance.'
      );
      return;
    }

    this.joining.set(true);
    this.error.set('');

    try {
      const membership = await this.people.accept(
        values.code.trim(),
        values.role
      );

      this.auth.currentUser.update(user => user
        ? {
            ...user,
            memberships: [
              ...user.memberships.filter(
                m => m.companyId !== membership.companyId
              ),
              membership
            ]
          }
        : user
      );

      await this.router.navigate([
        '/companies',
        membership.companyId
      ]);
    } catch (error) {
      this.error.set(this.auth.getErrorMessage(error));
    } finally {
      this.joining.set(false);
    }
  }
}