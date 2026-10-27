import { NavLink, Navigate, Route, Routes } from 'react-router-dom';
import ClaimsPage from './pages/ClaimsPage';
import ClaimDetailPage from './pages/ClaimDetailPage';
import PoliciesPage from './pages/PoliciesPage';
import PolicyholdersPage from './pages/PolicyholdersPage';
import ProductsPage from './pages/ProductsPage';
import LossRatioPage from './pages/LossRatioPage';
import QueuePage from './pages/QueuePage';
import LimitsReportPage from './pages/LimitsReportPage';

export default function App() {
  return (
    <div className="shell">
      <header className="topbar">
        <span className="brand">Harbourline</span>
        <nav>
          <NavLink to="/claims">Claims</NavLink>
          <NavLink to="/queue">Handler queue</NavLink>
          <NavLink to="/policies">Policies</NavLink>
          <NavLink to="/policyholders">Policyholders</NavLink>
          <NavLink to="/products">Products</NavLink>
          <NavLink to="/reports/loss-ratio">Loss ratio</NavLink>
          <NavLink to="/reports/limits-breaches">Limits report</NavLink>
        </nav>
      </header>
      <main>
        <Routes>
          <Route path="/" element={<Navigate to="/claims" replace />} />
          <Route path="/claims" element={<ClaimsPage />} />
          <Route path="/claims/:id" element={<ClaimDetailPage />} />
          <Route path="/queue" element={<QueuePage />} />
          <Route path="/reports/limits-breaches" element={<LimitsReportPage />} />
          <Route path="/policies"element={<PoliciesPage />} />
          <Route path="/policyholders" element={<PolicyholdersPage />} />
          <Route path="/products" element={<ProductsPage />} />
          <Route path="/reports/loss-ratio" element={<LossRatioPage />} />
        </Routes>
      </main>
    </div>
  );
}
