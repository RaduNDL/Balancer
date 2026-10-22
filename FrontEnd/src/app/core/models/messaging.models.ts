export interface Conversation {
  id: string;
  otherUserName: string;
  otherUserEmail: string;

  state: 'Pending' | 'Accepted' | 'Declined' | 'Blocked';

  isRequester: boolean;
  canRespond: boolean;
  canSend: boolean;
  hasHistory: boolean;
  updatedAtUtc: string;
}

export interface ChatMessage {
  // SQL folosește bigint.
  // Textul evită pierderea preciziei numerice în JavaScript.
  id: string;

  conversationId: string;
  senderUserId: number;
  clientMessageId: string;
  body: string;
  createdAtUtc: string;
}

export interface MessagePage {
  items: ChatMessage[];
  hasMore: boolean;
}