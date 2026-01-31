import type { AgentResponse, Repository, StreamingResponse, ConversationContext } from '../types';

const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:5074';

export const agentApi = {
  async sendMessage(
    message: string,
    repositoryId: string,
    conversationId?: string
  ): Promise<AgentResponse> {
    const response = await fetch(`${API_BASE_URL}/api/agent/chat`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        message,
        repositoryId,
        conversationId,
      }),
    });

    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.error || 'Failed to send message');
    }

    return response.json();
  },

  async *streamMessage(
    message: string,
    repositoryId: string,
    conversationId?: string,
    signal?: AbortSignal
  ): AsyncGenerator<StreamingResponse> {
    const response = await fetch(`${API_BASE_URL}/api/agent/chat/stream`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        message,
        repositoryId,
        conversationId,
      }),
      signal,
    });

    if (!response.ok) {
      throw new Error('Failed to start stream');
    }

    const reader = response.body?.getReader();
    if (!reader) {
      throw new Error('No response body');
    }

    const decoder = new TextDecoder();
    let buffer = '';

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const lines = buffer.split('\n');
      buffer = lines.pop() || '';

      for (const line of lines) {
        if (line.startsWith('data: ')) {
          const data = line.slice(6);
          if (data) {
            try {
              yield JSON.parse(data) as StreamingResponse;
            } catch {
              // Skip invalid JSON
            }
          }
        }
      }
    }
  },

  async getConversation(conversationId: string): Promise<ConversationContext> {
    const response = await fetch(
      `${API_BASE_URL}/api/agent/conversations/${conversationId}`
    );

    if (!response.ok) {
      throw new Error('Failed to get conversation');
    }

    return response.json();
  },

  async clearConversation(conversationId: string): Promise<void> {
    await fetch(`${API_BASE_URL}/api/agent/conversations/${conversationId}`, {
      method: 'DELETE',
    });
  },

  async getRepositories(): Promise<Repository[]> {
    const response = await fetch(`${API_BASE_URL}/api/ingestion/repositories`);

    if (!response.ok) {
      throw new Error('Failed to get repositories');
    }

    return response.json();
  },

  async getRepository(repositoryId: string): Promise<Repository> {
    const response = await fetch(
      `${API_BASE_URL}/api/ingestion/repositories/${repositoryId}`
    );

    if (!response.ok) {
      throw new Error('Failed to get repository');
    }

    return response.json();
  },

  async indexRepository(
    path: string,
    name?: string,
    description?: string
  ): Promise<Repository> {
    const response = await fetch(`${API_BASE_URL}/api/ingestion/repositories`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ path, name, description }),
    });

    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.error || 'Failed to index repository');
    }

    return response.json();
  },
};
