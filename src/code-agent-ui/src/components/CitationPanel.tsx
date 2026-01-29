import { useState } from 'react';
import type { Citation } from '../types';

interface CitationPanelProps {
  citations: Citation[];
  selectedIndex: number | null;
  onClose: () => void;
}

export function CitationPanel({ citations, selectedIndex, onClose }: CitationPanelProps) {
  const [activeTab, setActiveTab] = useState(selectedIndex ?? 0);

  if (citations.length === 0) {
    return null;
  }

  const activeCitation = citations[activeTab];

  return (
    <div className="w-96 bg-white dark:bg-gray-900 border-l border-gray-200 dark:border-gray-700 flex flex-col h-full">
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-gray-200 dark:border-gray-700">
        <h3 className="font-semibold text-gray-900 dark:text-gray-100">
          Sources ({citations.length})
        </h3>
        <button
          onClick={onClose}
          className="text-gray-500 hover:text-gray-700 dark:hover:text-gray-300"
        >
          <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
          </svg>
        </button>
      </div>

      {/* Tabs */}
      <div className="flex overflow-x-auto border-b border-gray-200 dark:border-gray-700">
        {citations.map((citation, index) => (
          <button
            key={citation.id}
            onClick={() => setActiveTab(index)}
            className={`px-4 py-2 text-sm font-medium whitespace-nowrap ${
              activeTab === index
                ? 'text-blue-600 border-b-2 border-blue-600'
                : 'text-gray-500 hover:text-gray-700 dark:hover:text-gray-300'
            }`}
          >
            [{index + 1}]
          </button>
        ))}
      </div>

      {/* Content */}
      {activeCitation && (
        <div className="flex-1 overflow-auto p-4">
          {/* File info */}
          <div className="mb-4">
            <div className="flex items-center space-x-2 mb-2">
              <svg className="w-4 h-4 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
              </svg>
              <span className="text-sm font-medium text-gray-900 dark:text-gray-100 break-all">
                {activeCitation.filePath}
              </span>
            </div>
            <div className="flex items-center space-x-4 text-xs text-gray-500">
              <span>
                Lines {activeCitation.startLine}
                {activeCitation.endLine !== activeCitation.startLine && `-${activeCitation.endLine}`}
              </span>
              {activeCitation.symbolName && (
                <span className="px-2 py-0.5 bg-gray-100 dark:bg-gray-800 rounded">
                  {activeCitation.symbolName}
                </span>
              )}
              {activeCitation.relevanceScore > 0 && (
                <span>
                  Score: {(activeCitation.relevanceScore * 100).toFixed(0)}%
                </span>
              )}
            </div>
          </div>

          {/* Code content */}
          <div className="relative">
            <div className="absolute top-2 right-2">
              <button
                onClick={() => navigator.clipboard.writeText(activeCitation.content)}
                className="p-1 text-gray-400 hover:text-gray-600 dark:hover:text-gray-300"
                title="Copy code"
              >
                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M8 5H6a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2v-1M8 5a2 2 0 002 2h2a2 2 0 002-2M8 5a2 2 0 012-2h2a2 2 0 012 2m0 0h2a2 2 0 012 2v3m2 4H10m0 0l3-3m-3 3l3 3" />
                </svg>
              </button>
            </div>
            <pre className="bg-gray-900 text-gray-100 p-4 rounded-lg overflow-x-auto text-sm">
              <code>{activeCitation.content}</code>
            </pre>
          </div>

          {/* Source type */}
          <div className="mt-4 text-xs text-gray-500">
            Found via: {activeCitation.sourceType.replace('_', ' ')}
          </div>
        </div>
      )}
    </div>
  );
}
