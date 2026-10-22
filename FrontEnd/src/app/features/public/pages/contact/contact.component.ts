import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-contact',
  standalone: true,
  imports: [RouterLink],
  template: `
    <main class="public-page">
      <header class="public-page-heading">
        <span class="eyebrow">Contact us</span>

        <h1>
          A question?<br>
          Let's find some <em>clarity.</em>
        </h1>

        <p class="public-lead">
          Learn where to start and how company access works.
        </p>
      </header>

      <div class="public-contact-grid">
        <section class="public-card">
          <span class="eyebrow">Support</span>
          <h2>Our contact channel</h2>

          <!-- Adresa demonstrativă este text, nu link de trimitere. -->
          <p class="public-email">{{ supportEmail }}</p>

          <p class="muted">
            Demo contact address. This mailbox cannot receive messages.
            A working support address will be published before launch.
          </p>

          <a class="text-link" routerLink="/about">
            Learn about Balancer →
          </a>
        </section>

        <section class="public-card" aria-labelledby="help-title">
          <h2 id="help-title">A few useful answers.</h2>

          <details class="public-faq">
            <summary>Do I need a company to register?</summary>
            <p>
              No. Your account starts as a personal account.
              You can decide what to do next after signing in.
            </p>
          </details>

          <details class="public-faq">
            <summary>How do I become an Owner?</summary>
            <p>
              Create a new workspace for your business.
              You become the Owner of that workspace.
            </p>
          </details>

          <details class="public-faq">
            <summary>How do I join an existing company?</summary>
            <p>
              Register your account, then ask the workspace Owner
              to add your account email and assign your role.
            </p>
          </details>

          <details class="public-faq">
            <summary>Does AI automatically grant company access?</summary>
            <p>
              No. It recommends a profile.
              Access to an existing company is approved by its Owner.
            </p>
          </details>
        </section>
      </div>
    </main>
  `
})
export class ContactComponent {
  // Înlocuim această valoare când există o adresă controlată de tine.
  readonly supportEmail = 'support@balancer.example';
}