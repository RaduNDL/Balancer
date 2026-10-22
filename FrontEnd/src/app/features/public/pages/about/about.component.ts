import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-about',
  standalone: true,
  imports: [RouterLink],
  template: `
    <main class="public-page">
      <header class="public-page-heading">
        <span class="eyebrow">About Balancer</span>

        <h1>
          A workspace built<br>
          around <em>people.</em>
        </h1>

        <p class="public-lead">
          Balancer is growing into a place where businesses
          organize their teams and everyday work.
        </p>
      </header>

      <section class="public-card public-section">
        <h2>You are a person before you have a company role.</h2>

        <p>
          Creating an account gives you a personal profile.
          It does not automatically make you a company owner,
          administrator or employee.
        </p>

        <p>
          Your role belongs to a specific workspace.
          You can own one workspace and work as a member of another.
        </p>
      </section>

      <section class="public-section" aria-labelledby="roles-title">
        <span class="eyebrow">Different responsibilities</span>
        <h2 id="roles-title">Five company roles.</h2>

        <div class="public-role-grid">
          @for (role of roles; track role.name) {
            <article class="public-card">
              <span class="role-badge" [attr.data-role]="role.name">
                {{ role.name }}
              </span>

              <h3>{{ role.title }}</h3>
              <p>{{ role.description }}</p>
            </article>
          }
        </div>

        <p class="field-help">
          These profiles describe the responsibilities the application
          is being built around. Financial and project modules are
          planned features.
        </p>
      </section>

      <section class="public-card public-section">
        <h2>Guidance from AI. Access approved by people.</h2>

        <p>
          The profile assistant suggests a starting point from
          your answers. Creating a workspace makes you its Owner.
          Joining an existing workspace requires approval from its Owner.
        </p>

        <a class="button button-primary" routerLink="/auth/register">
          Start with a personal account ↗
        </a>
      </section>
    </main>
  `
})
export class AboutComponent {
  // Aceste descrieri sunt conținut public, nu reguli de autorizare.
  readonly roles = [
    {
      name: 'Owner',
      title: 'Build and lead',
      description:
        'Own the workspace and manage its members and direction.'
    },
    {
      name: 'Admin',
      title: 'Keep things organized',
      description:
        'Support workspace administration and day-to-day operations.'
    },
    {
      name: 'Accountant',
      title: 'Understand the numbers',
      description:
        'Work with invoices, expenses and financial reporting.'
    },
    {
      name: 'Manager',
      title: 'Coordinate the work',
      description:
        'Organize projects, priorities, people and deadlines.'
    },
    {
      name: 'Employee',
      title: 'Make things happen',
      description:
        'Work on assigned tasks and the resources shared with you.'
    }
  ];
}