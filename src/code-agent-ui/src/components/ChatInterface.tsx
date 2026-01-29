import { useState, useRef, useEffect } from 'react';
import { useAgent } from '../hooks/useAgent';
import { MessageBubble } from './MessageBubble';
import { CitationPanel } from './CitationPanel';
import { RepositorySelector } from './RepositorySelector';
import type { Repository } from '../types';

export function ChatInterface() {
  const [selectedRepo, setSelectedRepo] = useState<Repository | null>(null);
  const [input, setInput] = useState('');
  const [showCitations, setShowCitations] = useState(false);
  const [selectedCitationIndex, setSelectedCitationIndex] = useState<number | null>(null);
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);

  const {
    messages,
    isLoading,
    citations,
    currentAction,
    sendMessage,
    cancelRequest,
    clearChat,
  } = useAgent(selectedRepo?.id ?? '');

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (input.trim() && !isLoading && selectedRepo) {
      sendMessage(input.trim());
      setInput('');
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSubmit(e);
    }
  };

  const handleCitationClick = (index: number) => {
    setSelectedCitationIndex(index);
    setShowCitations(true);
  };

  return (
    <div className="flex h-screen bg-gray-50 dark:bg-gray-900">
      {/* Main chat area */}
      <div className="flex-1 flex flex-col">
        {/* Header */}
        <header className="bg-white dark:bg-gray-800 border-b border-gray-200 dark:border-gray-700 px-6 py-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center space-x-4">
              <h1 className="text-xl font-bold text-gray-900 dark:text-gray-100">
                Code Agent
              </h1>
              <div className="w-64">
                <RepositorySelector
                  selectedId={selectedRepo?.id ?? null}
                  onSelect={setSelectedRepo}
                />
              </div>
            </div>
            <div className="flex items-center space-x-2">
              {citations.length > 0 && (
                <button
                  onClick={() => setShowCitations(!showCitations)}
                  className={`flex items-center space-x-1 px-3 py-2 rounded-lg text-sm ${
                    showCitations
                      ? 'bg-blue-100 text-blue-700 dark:bg-blue-900 dark:text-blue-300'
                      : 'bg-gray-100 text-gray-700 dark:bg-gray-700 dark:text-gray-300'
                  }`}
                >
                  <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                  </svg>
                  <span>Sources ({citations.length})</span>
                </button>
              )}
              <button
                onClick={clearChat}
                className="px-3 py-2 text-sm text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-gray-100"
              >
                Clear chat
              </button>
            </div>
          </div>
        </header>

        {/* Messages */}
        <div className="flex-1 overflow-y-auto p-6">
          {messages.length === 0 ? (
            <div className="flex flex-col items-center justify-center h-full text-gray-500 dark:text-gray-400">
              <svg className="w-16 h-16 mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" />
              </svg>
              <p className="text-lg font-medium mb-2">Start a conversation</p>
              <p className="text-sm text-center max-w-md">
                {selectedRepo
                  ? `Ask questions about the "${selectedRepo.name}" codebase`
                  : 'Select a repository to get started'}
              </p>
            </div>
          ) : (
            <>
              {messages.map((message) => (
                <MessageBubble
                  key={message.id}
                  message={message}
                  onCitationClick={handleCitationClick}
                />
              ))}
              <div ref={messagesEndRef} />
            </>
          )}
        </div>

        {/* Action indicator */}
        {currentAction && (
          <div className="px-6 py-2 bg-blue-50 dark:bg-blue-900/20 border-t border-blue-100 dark:border-blue-800">
            <div className="flex items-center space-x-2 text-sm text-blue-700 dark:text-blue-300">
              <svg className="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
              </svg>
              <span>{currentAction}</span>
            </div>
          </div>
        )}

        {/* Input area */}
        <div className="bg-white dark:bg-gray-800 border-t border-gray-200 dark:border-gray-700 p-4">
          <form onSubmit={handleSubmit} className="flex items-end space-x-4">
            <div className="flex-1">
              <textarea
                ref={inputRef}
                value={input}
                onChange={(e) => setInput(e.target.value)}
                onKeyDown={handleKeyDown}
                placeholder={
                  selectedRepo
                    ? "Ask about the code..."
                    : "Select a repository first"
                }
                disabled={!selectedRepo || isLoading}
                className="w-full px-4 py-3 border border-gray-300 dark:border-gray-600 rounded-lg resize-none focus:outline-none focus:ring-2 focus:ring-blue-500 bg-white dark:bg-gray-900 text-gray-900 dark:text-gray-100 disabled:opacity-50"
                rows={1}
              />
            </div>
            {isLoading ? (
              <button
                type="button"
                onClick={cancelRequest}
                className="px-4 py-3 bg-red-600 text-white rounded-lg hover:bg-red-700 focus:outline-none focus:ring-2 focus:ring-red-500"
              >
                Cancel
              </button>
            ) : (
              <button
                type="submit"
                disabled={!input.trim() || !selectedRepo}
                className="px-4 py-3 bg-blue-600 text-white rounded-lg hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 19l9 2-9-18-9 18 9-2zm0 0v-8" />
                </svg>
              </button>
            )}
          </form>
        </div>
      </div>

      {/* Citation panel */}
      {showCitations && (
        <CitationPanel
          citations={citations}
          selectedIndex={selectedCitationIndex}
          onClose={() => setShowCitations(false)}
        />
      )}
    </div>
  );
}
