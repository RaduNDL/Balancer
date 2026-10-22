import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

// Urcăm patru directoare până la app, apoi intrăm în core/services.
import { AuthService } from '../../../../core/services/auth.service';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [RouterLink],
  template: `
    <main class="public-page">
      <section class="public-hero" aria-labelledby="home-title">
        <div>
          <span class="eyebrow">A clearer place to begin</span>

          <h1 id="home-title">
            Your work.<br>
            Your team.<br>
            A little more <em>balance.</em>
          </h1>

          <p class="public-lead">
            Start with a personal account. Discover your work profile,
            create a business workspace or connect with your team.
          </p>

          <div class="public-actions">
            <!-- Destinația depinde de existența unei sesiuni. -->
            <a
              class="button button-primary"
              [routerLink]="auth.currentUser()
                ? '/onboarding'
                : '/auth/register'"
            >
              {{ auth.currentUser() ? 'Find my profile' : 'Create my account' }}
              ↗
            </a>

            <a class="button button-secondary" routerLink="/about">
              How it works
            </a>
          </div>

          <p class="field-help">
            Your account comes first. Your company role comes later.
          </p>
        </div>

        <!-- Acesta ilustrează fluxul, fără statistici inventate. -->
        <aside class="public-flow" aria-label="Your path in Balancer">
          <span class="eyebrow">Your next chapter</span>

          <div class="public-flow-step">
            <span class="question-number">01</span>
            <div>
              <h2>A personal account</h2>
              <p>A place to start, without a company role.</p>
            </div>
          </div>

          <div class="public-flow-step">
            <span class="question-number">02</span>
            <div>
              <h2>A little guidance</h2>
              <p>Describe your work to our profile assistant.</p>
            </div>
          </div>

          <div class="public-flow-step">
            <span class="question-number">03</span>
            <div>
              <h2>Your own direction</h2>
              <p>Create a workspace or connect with its owner.</p>
            </div>
          </div>
        </aside>
      </section>

      <section class="public-section" aria-labelledby="possibilities-title">
        <span class="eyebrow">One account. Different paths.</span>
        <h2 id="possibilities-title">Where do you want to begin?</h2>

        <div class="public-grid">
          <article class="public-card">
            <span class="public-card-symbol" aria-hidden="true">↗</span>
            <h3>Build your workspace</h3>
            <p>
              Create a workspace for your business and become
              its Owner.
            </p>
          </article>

          <article class="public-card">
            <span class="public-card-symbol" aria-hidden="true">◎</span>
            <h3>Join your team</h3>
            <p>
              Use your personal account to join a company.
              Its Owner approves your access and role.
            </p>
          </article>

          <article class="public-card">
            <span class="public-card-symbol" aria-hidden="true">✦</span>
            <h3>Find your starting point</h3>
            <p>
              Describe your responsibilities and get a suggested
              profile from the Balancer AI assistant.
            </p>
          </article>
        </div>
      </section>
    </main>
  `
})
export class HomeComponent {
  // Folosim sesiunea numai pentru personalizarea linkului principal.
  // Pagina rămâne publică.
  readonly auth = inject(AuthService);
}