import { Routes } from '@angular/router';

import { authGuard } from './core/guards/auth.guard';
import { onboardingGuard } from './core/guards/onboarding.guard';

export const routes: Routes = [
  // Paginile publice sunt accesibile fără autentificare.
  {
    path: '',
    pathMatch: 'full',
    title: 'Balancer | A place for your business',
    loadComponent: () =>
      import('./features/public/pages/home/home.component')
        .then(module => module.HomeComponent)
  },

  {
    path: 'about',
    title: 'About | Balancer',
    loadComponent: () =>
      import('./features/public/pages/about/about.component')
        .then(module => module.AboutComponent)
  },

  {
    path: 'contact',
    title: 'Contact | Balancer',
    loadComponent: () =>
      import('./features/public/pages/contact/contact.component')
        .then(module => module.ContactComponent)
  },

  {
    path: 'auth/login',
    title: 'Sign in | Balancer',
    loadComponent: () =>
      import('./features/auth/pages/login/login.component')
        .then(module => module.LoginComponent)
  },

  {
    path: 'auth/register',
    title: 'Create an account | Balancer',
    loadComponent: () =>
      import('./features/auth/pages/register/register.component')
        .then(module => module.RegisterComponent)
  },

  // Verificăm atât sesiunea, cât și finalizarea configurării inițiale.
  {
    path: 'dashboard',
    title: 'My workspaces | Balancer',
    canActivate: [authGuard, onboardingGuard],
    loadComponent: () =>
      import('./features/dashboard/pages/dashboard/dashboard.component')
        .then(module => module.DashboardComponent)
  },

  // Pagina de felicitare citește apartenența salvată de backend.
  {
    path: 'onboarding/success',
    title: 'Welcome to your workspace | Balancer',
    canActivate: [authGuard],
    loadComponent: () =>
      import(
        './features/onboarding/pages/onboarding-success/onboarding-success.component'
      ).then(module => module.OnboardingSuccessComponent)
  },

  // Chestionarul necesită autentificare.
  // Nu folosim onboardingGuard aici, deoarece ar putea redirecționa
  // utilizatorul înapoi către aceeași pagină.
  {
    path: 'onboarding',
    title: 'Find my profile | Balancer',
    canActivate: [authGuard],
    loadComponent: () =>
      import(
        './features/onboarding/pages/profile-setup/profile-setup.component'
      ).then(module => module.ProfileSetupComponent)
  },

  // Backendul verifică separat apartenența și rolul în firma cerută.
  {
    path: 'companies/:companyId',
    title: 'Company workspace | Balancer',
    canActivate: [authGuard, onboardingGuard],
    loadComponent: () =>
      import(
        './features/companies/pages/company-details/company-details.component'
      ).then(module => module.CompanyDetailsComponent)
  },
{
  path: 'messages',
  title: 'Messages | Balancer',

  // Mesageria este disponibilă și înaintea finalizării onboardingului.
  canActivate: [authGuard],

  loadComponent: () =>
    import('./features/messaging/pages/inbox/inbox.component')
      .then(module => module.InboxComponent)
},
  {
    path: 'people',
    title: 'People | Balancer',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/people/pages/people/people.component')
        .then(module => module.PeopleComponent)
  },
  // Ruta generală trebuie să fie ultima.
  {
    path: '**',
    redirectTo: ''
  }
];