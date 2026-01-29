import type { ChatMessage } from '../types';

interface MessageBubbleProps {
  message: ChatMessage;
  onCitationClick?: (index: number) => void;
}

export function MessageBubble({ message, onCitationClick }: MessageBubbleProps) {
  const isUser = message.role === 'user';

  return (
    <div className={`flex ${isUser ? 'justify-end' : 'justify-start'} mb-4`}>
      <div
        className={`max-w-[80%] rounded-lg px-4 py-3 ${
          isUser
            ? 'bg-blue-600 text-white'
            : 'bg-gray-100 dark:bg-gray-800 text-gray-900 dark:text-gray-100'
        }`}
      >
        {message.isStreaming && !message.content && (
          <div className="flex items-center space-x-2">
            <div className="animate-pulse flex space-x-1">
              <div className="w-2 h-2 bg-gray-400 rounded-full animate-bounce" />
              <div className="w-2 h-2 bg-gray-400 rounded-full animate-bounce delay-100" />
              <div className="w-2 h-2 bg-gray-400 rounded-full animate-bounce delay-200" />
            </div>
            <span className="text-sm text-gray-500">Thinking...</span>
          </div>
        )}

        <div className="prose prose-sm dark:prose-invert max-w-none">
          <MessageContent content={message.content} onCitationClick={onCitationClick} />
        </div>

        {message.citations && message.citations.length > 0 && (
          <div className="mt-3 pt-3 border-t border-gray-200 dark:border-gray-700">
            <p className="text-xs font-semibold text-gray-500 dark:text-gray-400 mb-2">
              Sources:
            </p>
            <div className="flex flex-wrap gap-1">
              {message.citations.map((citation, index) => (
                <button
                  key={citation.id}
                  onClick={() => onCitationClick?.(index)}
                  className="text-xs px-2 py-1 bg-blue-100 dark:bg-blue-900 text-blue-700 dark:text-blue-300 rounded hover:bg-blue-200 dark:hover:bg-blue-800 transition-colors"
                >
                  [{index + 1}] {citation.filePath.split('/').pop()}:{citation.startLine}
                </button>
              ))}
            </div>
          </div>
        )}

        <div className="mt-2 text-xs text-gray-400">
          {new Date(message.timestamp).toLocaleTimeString()}
        </div>
      </div>
    </div>
  );
}

interface MessageContentProps {
  content: string;
  onCitationClick?: (index: number) => void;
}

function MessageContent({ content, onCitationClick }: MessageContentProps) {
  // Parse markdown-style code blocks and citation references
  const parts = content.split(/(```[\s\S]*?```|\[(\d+)\])/g);

  return (
    <>
      {parts.map((part, index) => {
        // Code block
        if (part.startsWith('```')) {
          const match = part.match(/```(\w+)?\n?([\s\S]*?)```/);
          if (match) {
            const [, language, code] = match;
            return (
              <pre
                key={index}
                className="bg-gray-900 text-gray-100 p-3 rounded-md overflow-x-auto text-sm my-2"
              >
                {language && (
                  <div className="text-xs text-gray-400 mb-2">{language}</div>
                )}
                <code>{code}</code>
              </pre>
            );
          }
        }

        // Citation reference like [1]
        const citationMatch = part.match(/^\[(\d+)\]$/);
        if (citationMatch) {
          const citationIndex = parseInt(citationMatch[1], 10) - 1;
          return (
            <button
              key={index}
              onClick={() => onCitationClick?.(citationIndex)}
              className="inline-flex items-center justify-center w-5 h-5 text-xs bg-blue-100 dark:bg-blue-900 text-blue-700 dark:text-blue-300 rounded hover:bg-blue-200 dark:hover:bg-blue-800 transition-colors mx-0.5"
            >
              {citationMatch[1]}
            </button>
          );
        }

        // Regular text
        return <span key={index}>{part}</span>;
      })}
    </>
  );
}
