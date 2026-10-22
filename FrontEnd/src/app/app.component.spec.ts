import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AppComponent } from './app.component';
import { AuthService } from './core/services/auth.service';
import { AuthUser } from './core/models/auth.models';
import {
  MessagingService
} from './core/services/messaging.service';
describe('AppComponent navigation', () => {
  const user = signal<AuthUser | null>(null);

  beforeEach(async () => {
    user.set(null);

    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        provideRouter([]),
        // În testul navbarului nu deschidem o conexiune SignalR reală.
        {
          provide: MessagingService,
          useValue: {
            useUser: () => {},
            requestCount: () => 0
          }
        },
        {
          provide: AuthService,
          useValue: {
            currentUser: user,

            logout: async () => {
              user.set(null);
            },

            getErrorMessage: () => 'Request failed.'
          }
        }
      ]
    }).compileComponents();
  });

  it('shows sign-in navigation for a visitor', () => {
    const fixture = TestBed.createComponent(AppComponent);

    fixture.detectChanges();

    expect(
      fixture.nativeElement.querySelector(
        'a[href="/auth/login"]'
      )
    ).toBeTruthy();
  });

  it('shows the profile assistant for an authenticated user', () => {
    user.set({
      id: 1,
      fullName: 'Test Member',
      email: 'test@example.com',
      memberships: []
    });

    const fixture = TestBed.createComponent(AppComponent);

    fixture.detectChanges();

    expect(
      fixture.nativeElement.querySelector(
        'a[href="/onboarding"]'
      )
    ).toBeTruthy();
  });
});