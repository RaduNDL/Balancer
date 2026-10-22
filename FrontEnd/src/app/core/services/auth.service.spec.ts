import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';

import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        // Înregistrăm HttpClient, necesar serviciului.
        provideHttpClient(),

        // În teste înlocuim comunicarea HTTP cu varianta de test.
        // Acesta trebuie declarat după provideHttpClient.
        provideHttpClientTesting()
      ]
    });

    // Numele trebuie să corespundă clasei exportate.
    service = TestBed.inject(AuthService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});