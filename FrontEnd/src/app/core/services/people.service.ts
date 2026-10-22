import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

import {
  EditableProfile,
  PeoplePage,
  PersonProfile
} from '../models/people.models';

import { CompanyMembership } from '../models/auth.models';
import { OnboardingInvitation } from './onboarding.service';

@Injectable({ providedIn: 'root' })
export class PeopleService {
  private readonly http = inject(HttpClient);

  // Caută persoane și încarcă o singură pagină.
  list(search: string, page: number): Promise<PeoplePage> {
    const params = new HttpParams()
      .set('search', search)
      .set('page', page);

    return firstValueFrom(
      this.http.get<PeoplePage>('/api/people', {
        params,
        withCredentials: true
      })
    );
  }

  // Recitește profilul selectat din backend.
  get(id: number): Promise<PersonProfile> {
    return firstValueFrom(
      this.http.get<PersonProfile>(
        `/api/people/${id}`,
        { withCredentials: true }
      )
    );
  }

  // Citește setările contului propriu.
  me(): Promise<EditableProfile> {
    return firstValueFrom(
      this.http.get<EditableProfile>(
        '/api/people/me',
        { withCredentials: true }
      )
    );
  }

  // Metodă comună pentru operațiile care modifică date.
  private async write<T>(
    method: 'POST' | 'PUT',
    url: string,
    body: unknown
  ): Promise<T> {
    const csrf = await firstValueFrom(
      this.http.get<{ token: string }>(
        '/api/auth/csrf',
        { withCredentials: true }
      )
    );

    return firstValueFrom(
      this.http.request<T>(method, url, {
        body,
        withCredentials: true,
        headers: {
          'X-CSRF-TOKEN': csrf.token
        }
      })
    );
  }

  save(data: EditableProfile): Promise<{ message: string }> {
    return this.write('PUT', '/api/people/me', data);
  }

  invite(
    id: number,
    companyId: number,
    allowedRoles: string[]
  ): Promise<OnboardingInvitation> {
    return this.write('POST', `/api/people/${id}/invite`, {
      companyId,
      allowedRoles
    });
  }

  accept(
    invitationCode: string,
    role: string
  ): Promise<CompanyMembership> {
    return this.write('POST', '/api/people/accept-invitation', {
      invitationCode,
      role,
      acceptInvitation: true
    });
  }
}