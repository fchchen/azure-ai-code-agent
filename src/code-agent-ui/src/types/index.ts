export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  timestamp: string;
  citations?: Citation[];
  isStreaming?: boolean;
}

export interface Citation {
  id: string;
  filePath: string;
  startLine: number;
  endLine: number;
  content: string;
  symbolName?: string;
  relevanceScore: number;
  sourceType: string;
}

export interface AgentResponse {
  conversationId: string;
  message: string;
  citations: Citation[];
  reasoningSteps: AgentStep[];
  isComplete: boolean;
}

export interface AgentStep {
  stepNumber: number;
  thought: string;
  action?: string;
  actionInput?: string;
  observation?: string;
}

export interface StreamingResponse {
  type: 'thought' | 'action' | 'observation' | 'answer' | 'citation' | 'done';
  content: string;
  citation?: Citation;
  conversationId?: string;
}

export interface Repository {
  id: string;
  name: string;
  path: string;
  description?: string;
  indexedAt?: string;
  chunkCount: number;
  languages: string[];
}

export interface ConversationContext {
  id: string;
  repositoryId: string;
  messages: ChatMessage[];
  createdAt: string;
  updatedAt: string;
}
