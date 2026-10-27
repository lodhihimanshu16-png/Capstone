import type { ReactNode } from 'react';

interface Props {
  loading: boolean;
  error: string | null;
  empty?: boolean;
  emptyText?: string;
  loadingText?: string;
  onRetry?: () => void;
  children: ReactNode;
}

export default function PageState({ loading, error, empty, emptyText = 'Nothing to show.', loadingText = 'Loading...', onRetry, children }: Props) {
  if (loading) return <p className="state" role="status">{loadingText}</p>;
  if (error)
    return (
      <p className="state state-error" role="alert" aria-live="polite">
        {error} {onRetry && <button onClick={onRetry}>Retry</button>}
      </p>
    );
  if (empty) return <p className="state">{emptyText}</p>;
  return <>{children}</>;
}
