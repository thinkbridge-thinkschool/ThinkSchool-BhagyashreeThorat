export interface Quote {
  id: number;
  author: string;
  text: string;
}

export interface QuoteDetail {
  id: number;
  author: string;
  text: string;
  isDeleted: boolean;
  ownerId: number | null;
  authorId: number;
  authorRef: Record<string, unknown> | null;
}

export interface CreateQuote {
  author: string;
  text: string;
}
