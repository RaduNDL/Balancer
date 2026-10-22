import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

import {
  CompanyMembership,
  CompanyRole
} from '../models/auth.models';

// Exclude elimină Owner din rolurile care pot fi atribuite aici.
export type AssignableCompanyRole = Exclude<CompanyRole, 'Owner'>;

// Un membru returnat de backend.
export interface CompanyMember {
  userId: number;
  fullName: string;
  email: string;
  role: CompanyRole;
}

@Injectable({
  providedIn: 'root'
})
export class CompanyService {
  private readonly http = inject(HttpClient);

  // Informațiile firmei și rolul utilizatorului curent.
  getCompany(companyId: number): Promise<CompanyMembership> {
    return firstValueFrom(
      this.http.get<CompanyMembership>(
        `/api/companies/${companyId}`,
        { withCredentials: true }
      )
    );
  }

  // Lista membrilor, accesibilă doar Owner și Admin.
  getMembers(companyId: number): Promise<CompanyMember[]> {
    return firstValueFrom(
      this.http.get<CompanyMember[]>(
        `/api/companies/${companyId}/members`,
        { withCredentials: true }
      )
    );
  }

  // Adaugă un utilizator existent cu unul dintre cele patru roluri.
  async addMember(
    companyId: number,
    email: string,
    role: AssignableCompanyRole
  ): Promise<CompanyMember> {
    const csrf = await firstValueFrom(
      this.http.get<{ token: string }>(
        '/api/auth/csrf',
        { withCredentials: true }
      )
    );

    return firstValueFrom(
      this.http.post<CompanyMember>(
        `/api/companies/${companyId}/members`,
        { email, role },
        {
          withCredentials: true,
          headers: {
            'X-CSRF-TOKEN': csrf.token
          }
        }
      )
    );
  }
}