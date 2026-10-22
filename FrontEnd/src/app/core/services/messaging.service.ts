import {
  computed,
  inject,
  Injectable,
  signal
} from '@angular/core';

import {
  HttpClient,
  HttpErrorResponse,
  HttpParams
} from '@angular/common/http';

import { firstValueFrom } from 'rxjs';

import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel
} from '@microsoft/signalr';

import { AuthService } from './auth.service';

import {
  ChatMessage,
  Conversation,
  MessagePage
} from '../models/messaging.models';

@Injectable({
  providedIn: 'root'
})
export class MessagingService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  readonly inbox = signal<Conversation[]>([]);
  readonly state = signal('Offline');
  readonly error = signal('');

  // Pagina observă aceste valori pentru sincronizarea istoricului.
  readonly revision = signal(0);
  readonly resync = signal(0);

  readonly requestCount = computed(
    () => this.inbox().filter(item => item.canRespond).length
  );

  private userId: number | null = null;
  private epoch = 0;

  private hub?: HubConnection;
  private transition: Promise<void> = Promise.resolve();
  private retry?: ReturnType<typeof setTimeout>;

  private refreshing?: Promise<void>;
  private refreshAgain = false;

  useUser(userId: number | null): void {
    if (userId === this.userId) {
      return;
    }

    this.userId = userId;

    // Invalidează răspunsurile și conexiunile contului anterior.
    const epoch = ++this.epoch;

    clearTimeout(this.retry);

    this.inbox.set([]);
    this.error.set('');
    this.state.set('Offline');
    this.revision.update(value => value + 1);

    this.transition = this.transition
      .catch(() => {})
      .then(async () => {
        const old = this.hub;
        this.hub = undefined;

        if (old) {
          await old.stop();
        }

        if (epoch !== this.epoch || userId === null) {
          return;
        }

        const hub = new HubConnectionBuilder()
          .withUrl('/api/messaging-hub', {
            withCredentials: true
          })
          .withAutomaticReconnect([0, 2000, 5000, 15000])
          .configureLogging(LogLevel.Warning)
          .build();

        this.hub = hub;

        hub.on('MessagingChanged', () => {
          if (epoch === this.epoch) {
            void this.refreshInbox();
          }
        });

        hub.onreconnecting(() => {
          if (epoch === this.epoch) {
            this.state.set('Reconnecting');
          }
        });

        hub.onreconnected(() => {
          if (epoch !== this.epoch) {
            return;
          }

          this.state.set('Live');
          this.resync.update(value => value + 1);

          void this.refreshInbox();
        });

        hub.onclose(() => {
          if (epoch !== this.epoch) {
            return;
          }

          this.state.set('Offline');
          this.schedule(hub, epoch);
        });

        await this.start(hub, epoch);
      });
  }

  private schedule(
    hub: HubConnection,
    epoch: number
  ): void {
    clearTimeout(this.retry);

    this.retry = setTimeout(
      () => void this.start(hub, epoch),
      5000
    );
  }

  private async start(
    hub: HubConnection,
    epoch: number
  ): Promise<void> {
    if (
      epoch !== this.epoch
      || hub.state !== HubConnectionState.Disconnected
    ) {
      return;
    }

    this.state.set('Connecting');

    try {
      await hub.start();

      if (epoch !== this.epoch) {
        await hub.stop();
        return;
      }

      this.state.set('Live');
      this.resync.update(value => value + 1);

      await this.refreshInbox();
    } catch {
      if (epoch !== this.epoch) {
        return;
      }

      this.state.set('Offline');

      // Detectăm și o eventuală expirare a autentificării.
      await this.refreshInbox();

      if (epoch === this.epoch) {
        this.schedule(hub, epoch);
      }
    }
  }

  refreshInbox(): Promise<void> {
    this.refreshAgain = true;

    // Comasăm notificările care sosesc în timpul aceleiași cereri.
    if (this.refreshing) {
      return this.refreshing;
    }

    this.refreshing = (async () => {
      while (this.refreshAgain && this.userId !== null) {
        this.refreshAgain = false;

        const epoch = this.epoch;

        try {
          const inbox = await firstValueFrom(
            this.http.get<Conversation[]>(
              '/api/messaging/conversations',
              { withCredentials: true }
            )
          );

          if (epoch !== this.epoch) {
            continue;
          }

          this.inbox.set(inbox);
          this.error.set('');
          this.revision.update(value => value + 1);
        } catch (error) {
          if (epoch !== this.epoch) {
            continue;
          }

          this.error.set(
            this.auth.getErrorMessage(error)
          );

          if (
            error instanceof HttpErrorResponse
            && error.status === 401
          ) {
            this.auth.currentUser.set(null);
          }
        }
      }
    })().finally(() => {
      this.refreshing = undefined;
    });

    return this.refreshing;
  }

  private async post<T>(
    url: string,
    body: unknown
  ): Promise<T> {
    const csrf = await firstValueFrom(
      this.http.get<{ token: string }>(
        '/api/auth/csrf',
        { withCredentials: true }
      )
    );

    return firstValueFrom(
      this.http.post<T>(url, body, {
        withCredentials: true,
        headers: {
          'X-CSRF-TOKEN': csrf.token
        }
      })
    );
  }

  async request(email: string): Promise<string> {
    const result = await this.post<{
      conversationId: string;
    }>('/api/messaging/requests', { email });

    await this.refreshInbox();

    return result.conversationId;
  }
  // People folosește identificatorul persoanei, fără expunerea emailului.
  async requestUser(recipientUserId: number): Promise<string> {
    const result = await this.post<{
      conversationId: string;
    }>('/api/messaging/requests', {
      recipientUserId
    });

    await this.refreshInbox();

    return result.conversationId;
  }
  async decide(
    id: string,
    action: 'accept' | 'decline' | 'block'
  ): Promise<void> {
    await this.post(
      `/api/messaging/conversations/${id}/decision`,
      { action }
    );

    await this.refreshInbox();
  }

  messages(
    id: string,
    beforeId?: string,
    afterId?: string
  ): Promise<MessagePage> {
    let params = new HttpParams();

    if (beforeId) {
      params = params.set('beforeId', beforeId);
    }

    if (afterId) {
      params = params.set('afterId', afterId);
    }

    return firstValueFrom(
      this.http.get<MessagePage>(
        `/api/messaging/conversations/${id}/messages`,
        {
          params,
          withCredentials: true
        }
      )
    );
  }

  send(
    id: string,
    body: string,
    clientMessageId: string
  ): Promise<ChatMessage> {
    return this.post(
      `/api/messaging/conversations/${id}/messages`,
      {
        body,
        clientMessageId
      }
    );
  }
}