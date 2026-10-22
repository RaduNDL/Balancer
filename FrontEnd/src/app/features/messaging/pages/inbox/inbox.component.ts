import { DatePipe } from '@angular/common';

import {
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  signal,
  untracked,
  viewChild
} from '@angular/core';

import {
  FormBuilder,
  ReactiveFormsModule,
  Validators
} from '@angular/forms';

import {
  AuthService
} from '../../../../core/services/auth.service';

import {
  MessagingService
} from '../../../../core/services/messaging.service';

import {
  ChatMessage
} from '../../../../core/models/messaging.models';
import { ActivatedRoute } from '@angular/router';
@Component({
  selector: 'app-inbox',
  standalone: true,
  imports: [ReactiveFormsModule, DatePipe],
  templateUrl: './inbox.component.html'
})
export class InboxComponent {
  readonly auth = inject(AuthService);
  readonly chat = inject(MessagingService);

  private readonly fb = inject(FormBuilder);
private readonly route = inject(ActivatedRoute);
  private readonly messageList =
    viewChild<ElementRef<HTMLDivElement>>('messageList');

  readonly selectedId = signal<string | null>(null);

  readonly active = computed(() =>
    this.chat.inbox().find(
      item => item.id === this.selectedId()
    )
  );

  readonly messages = signal<ChatMessage[]>([]);
  readonly hasOlder = signal(false);
  readonly loadingMessages = signal(false);
  readonly loadingOlder = signal(false);
  readonly requesting = signal(false);
  readonly sending = signal(false);
  readonly deciding = signal(false);
  readonly errorMessage = signal('');
  readonly notice = signal('');

  readonly requestForm = this.fb.nonNullable.group({
    email: ['', [
      Validators.required,
      Validators.email,
      Validators.maxLength(254)
    ]]
  });

  readonly messageForm = this.fb.nonNullable.group({
    body: ['', [
      Validators.required,
      Validators.maxLength(2000)
    ]]
  });
  // Ultimul mesaj recuperat prin citirea istoricului.
  // Mesajele trimise separat nu avansează această poziție.

  private syncedThrough: string | null = null;
  private viewVersion = 0;
  private lastId: string | null = null;
  private lastResync = -1;

  private syncing = false;
  private pendingSync = false;
  private resetSync = false;

  private retryBody = '';
  private retryId = '';

  constructor() {
        // Deschidem o singură dată conversația cerută din People.
    const requestedId =
      this.route.snapshot.queryParamMap.get('conversation');

    let openedFromLink = false;

    effect(() => {
      const inbox = this.chat.inbox();

      if (
        !openedFromLink
        && requestedId
        && inbox.some(item => item.id === requestedId)
      ) {
        openedFromLink = true;

        untracked(() => {
          this.select(requestedId);
        });
      }
    });
    effect(() => {
      const user = this.auth.currentUser();
      const selected = this.selectedId();
      const revision = this.chat.revision();
      const resync = this.chat.resync();

      untracked(() => {
        if (!user) {
          this.select(null);
          return;
        }

        const reset = selected !== this.lastId
          || resync !== this.lastResync;

        this.lastId = selected;
        this.lastResync = resync;

        // Citirea semnalului de mai sus înregistrează dependența.
        void revision;

        if (selected) {
          void this.sync(reset);
        }
      });
    });
  }

  select(id: string | null): void {
    if (id === this.selectedId()) {
      return;
    }

    // Răspunsurile conversației anterioare devin invalide.
    ++this.viewVersion;
    // Fiecare conversație are propria poziție de sincronizare.
    this.syncedThrough = null;
    this.messages.set([]);
    this.hasOlder.set(false);
    this.selectedId.set(id);
    this.errorMessage.set('');
    this.notice.set('');
    this.messageForm.reset();

    this.retryBody = '';
    this.retryId = '';
  }

  private merge(items: ChatMessage[]): void {
    const unique = new Map(
      this.messages().map(message => [message.id, message])
    );

    for (const message of items) {
      unique.set(message.id, message);
    }

    this.messages.set(
      [...unique.values()].sort((first, second) =>
        BigInt(first.id) < BigInt(second.id)
          ? -1
          : BigInt(first.id) > BigInt(second.id)
            ? 1
            : 0
      )
    );
  }

  private scrollBottom(): void {
    requestAnimationFrame(() => {
      const element = this.messageList()?.nativeElement;

      if (element) {
        element.scrollTop = element.scrollHeight;
      }
    });
  }

    private async sync(reset: boolean): Promise<void> {
    this.pendingSync = true;
    this.resetSync ||= reset;

    // O singură sincronizare rulează la un moment dat.
    // Notificările suplimentare vor fi procesate de aceeași buclă.
    if (this.syncing) {
      return;
    }

    this.syncing = true;

    try {
      while (this.pendingSync) {
        this.pendingSync = false;

        const resetNow = this.resetSync;
        this.resetSync = false;

        const id = this.selectedId();
        const version = this.viewVersion;

        // Istoricul este disponibil numai după acceptarea conversației.
        if (!id || !this.active()?.hasHistory) {
          continue;
        }

        const element = this.messageList()?.nativeElement;

        // Derulăm automat numai dacă utilizatorul este aproape de final.
        const follow = !element
          || element.scrollHeight
            - element.scrollTop
            - element.clientHeight < 100;

        try {
          if (resetNow || this.syncedThrough === null) {
            this.loadingMessages.set(true);

            const page = await this.chat.messages(id);

            // Ignorăm răspunsurile pentru o conversație părăsită.
            if (
              id !== this.selectedId()
              || version !== this.viewVersion
            ) {
              continue;
            }

            // Combinăm istoricul cu mesajele deja afișate.
            // Astfel păstrăm un mesaj trimis în timpul încărcării.
            this.merge(page.items);
            this.hasOlder.set(page.hasMore);

            // Numai citirea istoricului avansează această poziție.
            // Pentru un istoric gol pornim de la 0.
            this.syncedThrough = page.items.at(-1)?.id ?? '0';

            this.scrollBottom();
          } else {
            let more = true;

            // Recuperăm toate paginile cu mesaje noi.
            while (more) {
              const page = await this.chat.messages(
                id,
                undefined,
                this.syncedThrough!
              );

              if (
                id !== this.selectedId()
                || version !== this.viewVersion
              ) {
                break;
              }

              this.merge(page.items);

              const lastFetched = page.items.at(-1);

              if (lastFetched) {
                this.syncedThrough = lastFetched.id;
              }

              // O pagină goală nu trebuie să producă o buclă fără sfârșit.
              more = page.hasMore && page.items.length > 0;
            }

            if (
              follow
              && id === this.selectedId()
              && version === this.viewVersion
            ) {
              this.scrollBottom();
            }
          }
        } catch (error) {
          // O eroare din conversația precedentă nu apare în cea nouă.
          if (
            id === this.selectedId()
            && version === this.viewVersion
          ) {
            this.errorMessage.set(
              this.auth.getErrorMessage(error)
            );
          }
        } finally {
          this.loadingMessages.set(false);
        }
      }
    } finally {
      this.syncing = false;
    }
  }

  async older(): Promise<void> {
    const id = this.selectedId();
    const first = this.messages()[0];

    if (!id || !first || this.loadingOlder()) {
      return;
    }

    const version = this.viewVersion;

    this.loadingOlder.set(true);

    try {
      const page = await this.chat.messages(
        id,
        first.id
      );

      if (
        id !== this.selectedId()
        || version !== this.viewVersion
      ) {
        return;
      }

      this.merge(page.items);
      this.hasOlder.set(page.hasMore);
    } catch (error) {
      this.errorMessage.set(
        this.auth.getErrorMessage(error)
      );
    } finally {
      this.loadingOlder.set(false);
    }
  }

  async request(): Promise<void> {
    if (this.requesting()) {
      return;
    }

    this.errorMessage.set('');

    if (this.requestForm.invalid) {
      this.errorMessage.set(
        'Enter the exact email of an existing Balancer account.'
      );
      return;
    }

    this.requesting.set(true);

    try {
      const id = await this.chat.request(
        this.requestForm.controls.email.value.trim()
      );

      this.requestForm.reset();
      this.select(id);
      this.notice.set('Conversation request opened.');
    } catch (error) {
      this.errorMessage.set(
        this.auth.getErrorMessage(error)
      );
    } finally {
      this.requesting.set(false);
    }
  }

  async decide(
    action: 'accept' | 'decline' | 'block'
  ): Promise<void> {
    const id = this.selectedId();

    if (!id || this.deciding()) {
      return;
    }

    this.deciding.set(true);
    this.errorMessage.set('');

    try {
      await this.chat.decide(id, action);
    } catch (error) {
      this.errorMessage.set(
        this.auth.getErrorMessage(error)
      );

      await this.chat.refreshInbox();
    } finally {
      this.deciding.set(false);
    }
  }

    async send(): Promise<void> {
    const id = this.selectedId();

    // Păstrăm textul exact pentru a detecta editările ulterioare.
    const draft = this.messageForm.controls.body.value;
    const body = draft.trim();

    if (
      !id
      || !this.active()?.canSend
      || this.sending()
      || this.messageForm.invalid
      || !body
    ) {
      return;
    }

    // La retrimiterea unui mesaj neconfirmat păstrăm identificatorul.
    // Backendul îl folosește pentru a evita salvarea unui duplicat.
    if (body !== this.retryBody || !this.retryId) {
      this.retryBody = body;
      this.retryId = crypto.randomUUID();
    }

    const version = this.viewVersion;

    this.sending.set(true);
    this.errorMessage.set('');

    try {
      const message = await this.chat.send(
        id,
        body,
        this.retryId
      );

      // Nu modificăm o altă conversație deschisă între timp.
      if (
        id !== this.selectedId()
        || version !== this.viewVersion
      ) {
        return;
      }

      // Afișăm mesajul confirmat de backend.
      // Această operație nu modifică syncedThrough.
      this.merge([message]);

      // Golim formularul numai dacă textul nu s-a schimbat.
      // Un mesaj nou început între timp rămâne în câmp.
      if (this.messageForm.controls.body.value === draft) {
        this.messageForm.reset();
      }

      this.retryBody = '';
      this.retryId = '';

      this.scrollBottom();

      // Recuperăm și mesajele primite simultan cu trimiterea.
      await this.sync(false);
      await this.chat.refreshInbox();
        } catch (error) {
      // Afișăm eroarea numai dacă este încă deschisă conversația originală.
      if (
        id === this.selectedId()
        && version === this.viewVersion
      ) {
        this.errorMessage.set(
          this.auth.getErrorMessage(error)
        );
      }

      // Recitim starea conversațiilor, inclusiv după o posibilă blocare.
      await this.chat.refreshInbox();
    } finally {
      // Permitem o nouă trimitere după încheierea operației.
      this.sending.set(false);
    }
  }
}
