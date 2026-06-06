// Shapes mirror the REAL Week-1 backend contract exactly. Field names are NOT
// invented — they match what the API returns/accepts byte-for-byte.

// GET /api/quotes  -> Quote[]  (list item: only id, author, text)
// POST /api/quotes -> Quote     (server echoes the created row)
export interface Quote {
  id: number;
  author: string;
  text: string;
}

// GET /api/quotes/{id} -> QuoteDetail (the richer single-record shape)
export interface QuoteDetail {
  id: number;
  author: string;
  text: string;
  isDeleted: boolean;
  ownerId: number | null;
  authorId: number;
  // Contract says `object | null`. We keep it strongly typed without `any` by
  // using an indexed object of unknowns — callers must narrow before use.
  authorRef: Record<string, unknown> | null;
}

// Body for POST /api/quotes — server assigns id/authorId/etc., so only the two
// writable fields are sent.
export interface CreateQuote {
  author: string;
  text: string;
}
