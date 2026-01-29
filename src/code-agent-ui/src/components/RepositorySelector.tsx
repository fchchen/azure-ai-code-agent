import { useState, useEffect } from 'react';
import { agentApi } from '../services/agentApi';
import type { Repository } from '../types';

interface RepositorySelectorProps {
  selectedId: string | null;
  onSelect: (repository: Repository) => void;
}

export function RepositorySelector({ selectedId, onSelect }: RepositorySelectorProps) {
  const [repositories, setRepositories] = useState<Repository[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isOpen, setIsOpen] = useState(false);

  useEffect(() => {
    loadRepositories();
  }, []);

  const loadRepositories = async () => {
    try {
      setIsLoading(true);
      setError(null);
      const repos = await agentApi.getRepositories();
      setRepositories(repos);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setIsLoading(false);
    }
  };

  const selectedRepo = repositories.find((r) => r.id === selectedId);

  return (
    <div className="relative">
      <button
        onClick={() => setIsOpen(!isOpen)}
        disabled={isLoading}
        className="flex items-center justify-between w-full px-4 py-2 text-left bg-white dark:bg-gray-800 border border-gray-300 dark:border-gray-600 rounded-lg hover:bg-gray-50 dark:hover:bg-gray-700 focus:outline-none focus:ring-2 focus:ring-blue-500"
      >
        <div className="flex items-center space-x-2">
          <svg className="w-5 h-5 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 7v10a2 2 0 002 2h14a2 2 0 002-2V9a2 2 0 00-2-2h-6l-2-2H5a2 2 0 00-2 2z" />
          </svg>
          <span className="text-gray-900 dark:text-gray-100">
            {isLoading
              ? 'Loading...'
              : selectedRepo
              ? selectedRepo.name
              : 'Select a repository'}
          </span>
        </div>
        <svg
          className={`w-5 h-5 text-gray-400 transition-transform ${isOpen ? 'rotate-180' : ''}`}
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
        </svg>
      </button>

      {isOpen && (
        <div className="absolute z-10 w-full mt-2 bg-white dark:bg-gray-800 border border-gray-300 dark:border-gray-600 rounded-lg shadow-lg max-h-60 overflow-auto">
          {error && (
            <div className="px-4 py-3 text-sm text-red-600 dark:text-red-400">
              {error}
            </div>
          )}

          {repositories.length === 0 && !error && (
            <div className="px-4 py-3 text-sm text-gray-500 dark:text-gray-400">
              No repositories indexed yet
            </div>
          )}

          {repositories.map((repo) => (
            <button
              key={repo.id}
              onClick={() => {
                onSelect(repo);
                setIsOpen(false);
              }}
              className={`w-full px-4 py-3 text-left hover:bg-gray-50 dark:hover:bg-gray-700 ${
                repo.id === selectedId ? 'bg-blue-50 dark:bg-blue-900/20' : ''
              }`}
            >
              <div className="flex items-center justify-between">
                <div>
                  <div className="font-medium text-gray-900 dark:text-gray-100">
                    {repo.name}
                  </div>
                  {repo.description && (
                    <div className="text-sm text-gray-500 dark:text-gray-400">
                      {repo.description}
                    </div>
                  )}
                </div>
                <div className="text-xs text-gray-400">
                  {repo.chunkCount} chunks
                </div>
              </div>
              <div className="flex items-center space-x-2 mt-1">
                {repo.languages.slice(0, 5).map((lang) => (
                  <span
                    key={lang}
                    className="px-2 py-0.5 text-xs bg-gray-100 dark:bg-gray-700 text-gray-600 dark:text-gray-300 rounded"
                  >
                    {lang}
                  </span>
                ))}
                {repo.languages.length > 5 && (
                  <span className="text-xs text-gray-400">
                    +{repo.languages.length - 5} more
                  </span>
                )}
              </div>
            </button>
          ))}

          <div className="border-t border-gray-200 dark:border-gray-700">
            <button
              onClick={loadRepositories}
              className="w-full px-4 py-2 text-sm text-blue-600 dark:text-blue-400 hover:bg-gray-50 dark:hover:bg-gray-700"
            >
              Refresh list
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
