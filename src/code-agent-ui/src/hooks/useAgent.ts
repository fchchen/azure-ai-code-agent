import { useState, useCallback, useRef } from 'react';
import { agentApi } from '../services/agentApi';
import type { ChatMessage, Citation, StreamingResponse } from '../types';

export function useAgent(repositoryId: string) {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [citations, setCitations] = useState<Citation[]>([]);
  const [conversationId, setConversationId] = useState<string | undefined>();
  const [currentAction, setCurrentAction] = useState<string | null>(null);
  const abortControllerRef = useRef<AbortController | null>(null);

  const sendMessage = useCallback(
    async (content: string) => {
      if (!content.trim() || !repositoryId) return;

      // Add user message
      const userMessage: ChatMessage = {
        id: crypto.randomUUID(),
        role: 'user',
        content,
        timestamp: new Date().toISOString(),
      };
      setMessages((prev) => [...prev, userMessage]);
      setIsLoading(true);
      setCitations([]);
      setCurrentAction(null);

      // Create placeholder for assistant message
      const assistantMessageId = crypto.randomUUID();
      const assistantMessage: ChatMessage = {
        id: assistantMessageId,
        role: 'assistant',
        content: '',
        timestamp: new Date().toISOString(),
        isStreaming: true,
        citations: [],
      };
      setMessages((prev) => [...prev, assistantMessage]);

      try {
        abortControllerRef.current = new AbortController();
        let fullContent = '';
        const newCitations: Citation[] = [];

        for await (const chunk of agentApi.streamMessage(
          content,
          repositoryId,
          conversationId,
          abortControllerRef.current.signal
        )) {
          handleStreamChunk(chunk, assistantMessageId, fullContent, newCitations, (updatedContent) => {
            fullContent = updatedContent;
          });
        }

        // Finalize the message
        setMessages((prev) =>
          prev.map((msg) =>
            msg.id === assistantMessageId
              ? { ...msg, content: fullContent, isStreaming: false, citations: newCitations }
              : msg
          )
        );
        setCitations(newCitations);
      } catch (error) {
        if ((error as Error).name === 'AbortError') {
          // User cancelled
          setMessages((prev) =>
            prev.map((msg) =>
              msg.id === assistantMessageId
                ? { ...msg, content: msg.content + '\n\n*Response cancelled*', isStreaming: false }
                : msg
            )
          );
        } else {
          // Error occurred
          setMessages((prev) =>
            prev.map((msg) =>
              msg.id === assistantMessageId
                ? { ...msg, content: `Error: ${(error as Error).message}`, isStreaming: false }
                : msg
            )
          );
        }
      } finally {
        setIsLoading(false);
        setCurrentAction(null);
        abortControllerRef.current = null;
      }
    },
    [repositoryId, conversationId]
  );

  const handleStreamChunk = (
    chunk: StreamingResponse,
    messageId: string,
    currentContent: string,
    citationsList: Citation[],
    updateContent: (content: string) => void
  ) => {
    switch (chunk.type) {
      case 'action':
        try {
          const action = JSON.parse(chunk.content);
          setCurrentAction(`Using ${action.tool}...`);
        } catch {
          setCurrentAction(chunk.content);
        }
        break;

      case 'observation':
        setCurrentAction(null);
        break;

      case 'answer':
        const newContent = currentContent + chunk.content;
        updateContent(newContent);
        setMessages((prev) =>
          prev.map((msg) =>
            msg.id === messageId ? { ...msg, content: newContent } : msg
          )
        );
        break;

      case 'citation':
        if (chunk.citation) {
          citationsList.push(chunk.citation);
        }
        break;

      case 'done':
        if (chunk.conversationId) {
          setConversationId(chunk.conversationId);
        }
        break;
    }
  };

  const cancelRequest = useCallback(() => {
    abortControllerRef.current?.abort();
  }, []);

  const clearChat = useCallback(async () => {
    if (conversationId) {
      await agentApi.clearConversation(conversationId);
    }
    setMessages([]);
    setCitations([]);
    setConversationId(undefined);
  }, [conversationId]);

  return {
    messages,
    isLoading,
    citations,
    currentAction,
    conversationId,
    sendMessage,
    cancelRequest,
    clearChat,
  };
}
