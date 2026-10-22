import { inject, Injectable, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

import {
  AuthUser,
  CompanyMembership,
  LoginRequest,
  MessageResponse,
  RegisterRequest
} from '../models/auth.models';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly baseUrl = '/api/auth';

  // Profilul este păstrat doar în memoria aplicației.
  // Cookie-ul este gestionat separat de browser.
  readonly currentUser = signal<AuthUser | null>(null);

  // Metodă comună pentru cererile POST.
  // Primește adresa completă relativă, de exemplu /api/companies.
  private async post<T>(
    url: string,
    body: unknown
  ): Promise<T> {
    // Cerem un token proaspăt pentru identitatea curentă.
    const csrf = await firstValueFrom(
      this.http.get<{ token: string }>(
        `${this.baseUrl}/csrf`,
        { withCredentials: true }
      )
    );

    return firstValueFrom(
      this.http.post<T>(
        url,
        body,
        {
          withCredentials: true,
          headers: {
            'X-CSRF-TOKEN': csrf.token
          }
        }
      )
    );
  }

  // Creează contul și firma prin endpointul de înregistrare.
  register(data: RegisterRequest): Promise<MessageResponse> {
    return this.post<MessageResponse>(
      `${this.baseUrl}/register`,
      data
    );
  }

  // Backendul verifică parola și emite cookie-ul.
  // După login încărcăm profilul și apartenențele.
  async login(data: LoginRequest): Promise<AuthUser> {
    await this.post<MessageResponse>(
      `${this.baseUrl}/login`,
      data
    );

    return this.loadCurrentUser();
  }

  // Citește profilul folosind cookie-ul trimis de browser.
  async loadCurrentUser(): Promise<AuthUser> {
    try {
      const user = await firstValueFrom(
        this.http.get<AuthUser>(
          `${this.baseUrl}/me`,
          { withCredentials: true }
        )
      );

      this.currentUser.set(user);

      return user;
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this.currentUser.set(null);
      }

      throw error;
    }
  }

  // Creează o firmă pentru utilizatorul deja autentificat.
  // Răspunsul conține rolul real atribuit de backend.
  createCompany(name: string): Promise<CompanyMembership> {
    return this.post<CompanyMembership>(
      '/api/companies',
      { name }
    );
  }

  // Cere backendului să elimine cookie-ul de autentificare.
  async logout(): Promise<void> {
    try {
      await this.post<MessageResponse>(
        `${this.baseUrl}/logout`,
        {}
      );
    } catch (error) {
      // Dacă sesiunea a expirat deja, considerăm logoutul încheiat.
      if (!(error instanceof HttpErrorResponse && error.status === 401)) {
        throw error;
      }
    }

    this.currentUser.set(null);
  }

  // Transformă erorile HTTP în mesaje pentru utilizator.
  getErrorMessage(error: unknown): string {
    if (!(error instanceof HttpErrorResponse)) {
      return 'An unexpected error occurred.';
    }

    if (error.status === 0) {
      return 'Cannot reach the server. Check that the backend is running.';
    }

    if (error.status === 429) {
      return 'Too many requests. Please wait and try again.';
    }

    if (typeof error.error?.message === 'string') {
      return error.error.message;
    }

    const validationErrors = error.error?.errors as
      | Record<string, string[]>
      | undefined;

    if (validationErrors) {
      return Object.values(validationErrors).flat().join(' ');
    }

    if (error.status === 401) {
      return 'Your session has expired. Please sign in again.';
    }

    if (error.status === 403) {
      return 'You do not have permission to perform this action.';
    }

    return 'The request failed. Please try again.';
  }
}