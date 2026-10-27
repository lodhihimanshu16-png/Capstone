export default function StatusBadge({ status }: { status?: string | null }) {
  const label = status || 'Unknown';
  const css = label.toLowerCase().replace(/[^a-z0-9]+/g, '-');
  return <span className={`badge badge-${css}`}>{label}</span>;
}
