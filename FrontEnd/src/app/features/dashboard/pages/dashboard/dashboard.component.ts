import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { CompanyRole } from '../../../../core/models/auth.models';
import { AuthService } from '../../../../core/services/auth.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,

  // Permite utilizarea routerLink în HTML.
  // Navigarea se face fără reîncărcarea întregii pagini.
  imports: [RouterLink],

  // Fișierul HTML folosit pentru afișarea dashboardului.
  templateUrl: './dashboard.component.html'
})
export class DashboardComponent {
  // Injectăm serviciul care păstrează utilizatorul autentificat.
  // auth.currentUser() oferă profilul și apartenențele sale la firme.
  readonly auth = inject(AuthService);

  // Asociem fiecărui rol o descriere pentru interfață.
  // Record obligă obiectul să conțină toate rolurile din CompanyRole.
  // Permisiunile reale sunt verificate separat în backend.
  readonly roleDescriptions: Record<CompanyRole, string> = {
    Owner: 'Company ownership and full workspace management.',
    Admin: 'Company operations and member management.',
    Accountant: 'Financial records, invoices and financial reports.',
    Manager: 'Clients, projects and team coordination.',
    Employee: 'Assigned tasks and permitted company resources.'
  };
}