import { useState } from 'react';
import { Link } from 'react-router-dom';
import { handlersApi } from '../api/handlers';
import { reportsApi } from '../api/reports';
import PageState from '../components/PageState';
import { money } from '../components/format';
import { validateDateRange } from '../components/validation';
import { useApi } from '../hooks/useApi';

const KIND_LABEL = { BlockedAttempt: 'Blocked attempt', ApprovedAboveLimit: 'Approved above limit' };

// Default period: the last 365 days up to today, so the screen does not go stale when the year changes.
function daysAgo(days: number): string {
  const d = new Date();
  d.setDate(d.getDate() - days);
  return d.toISOString().slice(0, 10);
}

export default function LimitsReportPage() {
  const [from, setFrom] = useState(daysAgo(365));
  const [to, setTo] = useState(daysAgo(0));
  const [regionId, setRegionId] = useState('');
  const [reload, setReload] = useState(0);
  const regions = useApi(() => handlersApi.regions(), []);
  const rangeError = validateDateRange(from, to);
  const { data, loading, error } = useApi(
    () => (rangeError ? Promise.resolve([]) : reportsApi.limitsBreaches(from, to, regionId ? Number(regionId) : undefined)),
    [from, to, regionId, reload],
  );

  return (
    <section>
      <h1>Limits report</h1>
      <div className="toolbar">
        <label>From <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
        <label>To <input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
        <label>
          Region{' '}
          <select value={regionId} onChange={(e) => setRegionId(e.target.value)}>
            <option value="">All</option>
            {regions.data?.map((r) => (
              <option key={r.id} value={r.id}>{r.name}</option>
            ))}
          </select>
        </label>
      </div>
      <div aria-live="polite">{rangeError && <p className="field-error">{rangeError}</p>}</div>
      {regions.error && <p className="state state-error" role="alert">Regions could not be loaded: {regions.error}</p>}
      {!rangeError && (
        <PageState
          loading={loading}
          error={error}
          empty={data?.length === 0}
          emptyText="No approvals above authority in this period."
          loadingText="Loading the report..."
          onRetry={() => setReload(reload + 1)}
        >
          <table>
            <thead>
              <tr>
                <th>Claim</th><th>Kind</th><th className="num">Amount</th><th className="num">Limit</th><th className="num">Over by</th>
                <th>Who</th><th>Grade</th><th>Region</th><th>When</th><th>Reason</th>
              </tr>
            </thead>
            <tbody>
              {data?.map((b, i) => (
                <tr key={`${b.claimId}-${b.occurredAt}-${i}`}>
                  <td><Link to={`/claims/${b.claimId}`}>{b.claimNumber}</Link></td>
                  <td>{KIND_LABEL[b.kind] ?? b.kind}</td>
                  <td className="num">{money(b.amount)}</td>
                  <td className="num">{money(b.limit)}</td>
                  <td className="num">{money(b.overBy)}</td>
                  <td>{b.userName}</td>
                  <td>{b.gradeCode ?? '-'}</td>
                  <td>{b.regionCode}</td>
                  <td>{b.occurredAt}</td>
                  <td>{b.reason ?? ''}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </PageState>
      )}
    </section>
  );
}
