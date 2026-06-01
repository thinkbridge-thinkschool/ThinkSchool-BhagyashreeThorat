// Mirrors the real backend quote contract exactly: id, text, author.
// NOTE: `id` is typed `number` (typical .NET int PK). If your backend uses a
// Guid, change this to `string` — nothing else needs to change.
export interface Quote {
  id: number;
  text: string;
  author: string;
}

// Payload for POST /api/quotes — server assigns the id, so it is omitted.
export type CreateQuote = Omit<Quote, 'id'>;
