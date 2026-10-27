import { useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { claimsApi } from '../api/claims';
import { handlersApi } from '../api/handlers';
import type { ChangeStatusBody, Claim, NextAction, RequiredField } from '../api/types';
import { validateAmount, validateReason, validateSettlement } from '../components/validation';
import PageState from '../components/PageState';
import StatusBadge from '../components/StatusBadge';
import { money } from '../components/format';
import { useApi } from '../hooks/useApi';

const FIELD_LABEL: Record<RequiredField, string> = {
  estimatedAmount: 'Estimated amount',
  approvedAmount: 'Approved amount',
  settlementAmount: 'Settlement amount',
  reason: 'Reason',
};

function validateField(field: RequiredField, value: string, claim: Claim): string | null {
  switch (field) {
    case 'reason': return validateReason(value);
    case 'settlementAmount': return validateSettlement(value, claim.approvedAmount);
    default: return validateAmount(value, FIELD_LABEL[field]);
  }
}

interface ActionFormProps {
  claim: Claim;
  action: NextAction;
  actingUserId: number | null;
  onDone: () => void;
  onCancel: () => void;
}

function ActionForm({ claim, action, actingUserId, onDone, onCancel }: ActionFormProps) {
  const [values, setValues] = useState<Record<string, string>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [serverError, setServerError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setServerError(null);
    const found: Record<string, string> = {};
    if (actingUserId == null) found.acting = 'Choose who you are acting as first.';
    for (const f of action.requires) {
      const message = validateField(f, values[f] ?? '', claim);
      if (message) found[f] = message;
    }
    setErrors(found);
    if (Object.keys(found).length > 0 || actingUserId == null) return;

    const body: ChangeStatusBody = { status: action.toStatus, changedByUserId: actingUserId };
    for (const f of action.requires) {
      if (f === 'reason') body.reason = (values.reason ?? '').trim();
      else body[f] = Number(values[f]);
    }
    setBusy(true);
    try {
      if (action.action === 'Reopen') await claimsApi.reopen(claim.id, { changedByUserId: actingUserId, reason: body.reason ?? '' });
      else await claimsApi.changeStatus(claim.id, body);
      onDone();
    } catch (err) {
      setServerError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit} noValidate aria-label={`${action.action} form`}>
      <h3>{action.action}</h3>
      {action.requires.map((f) => (
        <div className="form-row" key={f}>
          <label htmlFor={`field-${f}`}>{FIELD_LABEL[f]}</label>
          {f === 'reason' ? (
            <textarea id={`field-${f}`} rows={3} value={values[f] ?? ''} aria-invalid={!!errors[f]}
              onChange={(e) => setValues({ ...values, [f]: e.target.value })} />
          ) : (
            <input id={`field-${f}`} type="text" inputMode="decimal" value={values[f] ?? ''} aria-invalid={!!errors[f]}
              onChange={(e) => setValues({ ...values, [f]: e.target.value })} />
          )}
          {errors[f] && <span className="field-error">{errors[f]}</span>}
        </div>
      ))}
      <div aria-live="polite">
        {errors.acting && <p className="field-error">{errors.acting}</p>}
        {serverError && <p className="field-error" role="alert">{serverError}</p>}
      </div>
      <button type="submit" disabled={busy}>{busy ? 'Saving...' : `Confirm ${action.action}`}</button>{' '}
      <button type="button" onClick={onCancel} disabled={busy}>Cancel</button>
    </form>
  );
}

export default function ClaimDetailPage() {
  const id = Number(useParams().id);
  const [reload, setReload] = useState(0);
  const [actingUserId, setActingUserId] = useState<number | null>(null);
  const [openAction, setOpenAction] = useState<string | null>(null);
  const claim = useApi(() => claimsApi.get(id), [id, reload]);
  const notes = useApi(() => claimsApi.notes(id), [id]);
  const next = useApi(() => claimsApi.nextActions(id, actingUserId ?? undefined), [id, actingUserId, reload]);
  const handlers = useApi(() => handlersApi.handlers(), []);
  const users = useApi(() => handlersApi.users(), []);

  const headOfClaims = users.data?.filter((u) => u.role === 'Manager' && u.active) ?? [];
  const actors = [
    ...(handlers.data?.filter((h) => h.active).map((h) => ({ userId: h.userId, label: `${h.name} (${h.gradeCode})` })) ?? []),
    ...headOfClaims.map((u) => ({ userId: u.id, label: `${u.displayName} (Head of Claims)` })),
  ];
  const userName = (userId: number | null) =>
    userId == null ? '-' : (users.data?.find((u) => u.id === userId)?.displayName ?? `User ${userId}`);
  const selected =next.data?.actions.find((a) => a.action === openAction);
  const done = () => {
    setOpenAction(null);
    setReload(reload + 1);
  };

  return (
    <section>
      <p><Link to="/claims">Back to claims</Link></p>
      <PageState loading={claim.loading} error={claim.error}>
        {claim.data && (
          <>
            <h1>{claim.data.claimNumber} <StatusBadge status={claim.data.status} /></h1>
            <dl className="facts">
              <dt>Type</dt><dd>{claim.data.claimType}</dd>
              <dt>Loss date</dt><dd>{claim.data.lossDate}</dd>
              <dt>Estimate</dt><dd>{money(claim.data.estimatedAmount)}</dd>
              <dt>Approved</dt><dd>{money(claim.data.approvedAmount)}</dd>
              <dt>Paid</dt><dd>{money(claim.data.paidAmount)}</dd>
              <dt>Excess</dt><dd>{money(claim.data.excess)}</dd>
              <dt>Settlement</dt><dd>{money(claim.data.settlementAmount)}</dd>
              <dt>Assessed by</dt><dd>{userName(claim.data.assessedByUserId)}</dd>
              <dt>Approved by</dt><dd>{userName(claim.data.approvedByUserId)}</dd>
              <dt>Reopened</dt><dd>{claim.data.reopenedCount} time(s)</dd>
            </dl>
            <p>{claim.data.description}</p>
          </>
        )}
      </PageState>

      <h2>Next steps</h2>
      <div className="panel">
        <div className="form-row">
          <label htmlFor="acting-as">Acting as</label>
          <select id="acting-as" value={actingUserId ?? ''}
            onChange={(e) => { setActingUserId(e.target.value ? Number(e.target.value) : null); setOpenAction(null); }}>
            <option value="">Choose a user...</option>
            {actors.map((a) => (
              <option key={a.userId} value={a.userId}>{a.label}</option>
            ))}
          </select>
          {(handlers.error || users.error) && (
            <span className="field-error" role="alert">Users could not be loaded: {handlers.error ?? users.error}</span>
          )}
        </div>
        <PageState loading={next.loading} error={next.error} loadingText="Loading next steps..." onRetry={() => setReload(reload + 1)}>
          {next.data && (
            <>
              <p>{next.data.nextStep}</p>
              <p>
                {next.data.dueBy && <>Due by {next.data.dueBy}. </>}
                Day {next.data.daysInStage} in this stage{next.data.targetDays != null && <> (target {next.data.targetDays})</>}.{' '}
                {next.data.overdue && <span className="flag-overdue">Overdue</span>}
              </p>
              {next.data.actions.length === 0 ? (
                <p className="state">No further actions. This claim is final.</p>
              ) : (
                <div className="actions">
                  {next.data.actions.map((a) => (
                    <span key={a.action}>
                      <button disabled={!a.allowed} aria-pressed={openAction === a.action}
                        onClick={() => claim.data && setOpenAction(a.action)}>
                        {a.action}
                      </button>
                      {!a.allowed && a.blockedReason && <small className="blocked-reason">{a.blockedReason}</small>}
                    </span>
                  ))}
                </div>
              )}
              {selected && claim.data && (
                <ActionForm key={selected.action} claim={claim.data} action={selected} actingUserId={actingUserId}
                  onDone={done} onCancel={() => setOpenAction(null)} />
              )}
            </>
          )}
        </PageState>
      </div>
      <h2>Notes</h2>
      <PageState loading={notes.loading} error={notes.error} empty={notes.data?.length === 0} emptyText="No notes on this claim yet.">
        <ul className="notes">
          {notes.data?.map((n) => (
            <li key={n.id}><strong>{n.noteType}</strong> <small>{n.createdAt}</small><br />{n.body}</li>
          ))}
        </ul>
      </PageState>
    </section>
  );
}
