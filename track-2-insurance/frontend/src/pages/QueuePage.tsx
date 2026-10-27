import { useState } from 'react';
import { Link } from 'react-router-dom';
import { claimsApi } from '../api/claims';
import { handlersApi } from '../api/handlers';
import PageState from '../components/PageState';
import StatusBadge from '../components/StatusBadge';
import { money } from '../components/format';
import { useApi } from '../hooks/useApi';

export default function QueuePage() {
  const [handlerId, setHandlerId] = useState('');
  const [regionId, setRegionId] = useState('');
  const [page, setPage] = useState(1);
  const [reload, setReload] = useState(0);
  const handlers = useApi(() => handlersApi.handlers(), []);
  const regions = useApi(() => handlersApi.regions(), []);
  const { data, loading, error } = useApi(
    () => claimsApi.queue({ handlerId: handlerId ? Number(handlerId) : undefined, regionId: regionId ? Number(regionId) : undefined, page }),
    [handlerId, regionId, page, reload],
  );

  return (
    <section>
      <h1>Handler queue</h1>
      <div className="toolbar">
        <label>
          Handler{' '}
          <select value={handlerId} onChange={(e) => { setHandlerId(e.target.value); setPage(1); }}>
            <option value="">All</option>
            {handlers.data?.map((h) => (
              <option key={h.id} value={h.id}>{h.name}</option>
            ))}
          </select>
        </label>
        <label>
          Region{' '}
          <select value={regionId} onChange={(e) => { setRegionId(e.target.value); setPage(1); }}>
            <option value="">All</option>
            {regions.data?.map((r) => (
              <option key={r.id} value={r.id}>{r.name}</option>
            ))}
          </select>
        </label>
      </div>
      {(handlers.error || regions.error) && (
        <p className="state state-error" role="alert">Filter lists could not be loaded: {handlers.error ?? regions.error}</p>
      )}
      <PageState
        loading={loading}
        error={error}
        empty={data?.items.length === 0}
        emptyText="No open claims for this selection."
        loadingText="Loading the queue..."
        onRetry={() => setReload(reload + 1)}
      >
        <table>
          <thead>
            <tr>
              <th>Claim</th><th>Status</th><th>Policyholder</th><th>Product</th><th>Handler</th>
              <th className="num">Estimate</th><th>Reported</th><th className="num">Days in stage</th><th className="num">Target</th><th>Overdue</th>
            </tr>
          </thead>
          <tbody>
            {data?.items.map((q) => (
              <tr key={q.claimId} className={q.overdue ? 'overdue' : undefined}>
                <td><Link to={`/claims/${q.claimId}`}>{q.claimNumber}</Link></td>
                <td><StatusBadge status={q.status} /></td>
                <td>{q.policyholderName}</td>
                <td>{q.productName}</td>
                <td>{q.handlerName ?? 'Unassigned'}</td>
                <td className="num">{money(q.estimatedAmount)}</td>
                <td>{q.reportedDate}</td>
                <td className="num">{q.daysInStage}</td>
                <td className="num">{q.targetDays}</td>
                <td>{q.overdue ? <span className="flag-overdue">Overdue by {q.daysOverTarget} day(s)</span> : 'No'}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <div className="pager">
          <button disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
          <span>Page {page} of {Math.max(1, Math.ceil((data?.total ?? 0) / (data?.pageSize ?? 25)))}</span>
          <button disabled={!data || page * data.pageSize >= data.total} onClick={() => setPage(page + 1)}>Next</button>
        </div>
      </PageState>
    </section>
  );
}
