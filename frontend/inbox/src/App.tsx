import { Suspense, lazy } from "react";
import { Link, Navigate, Route, Routes, useLocation } from "react-router-dom";
import { useAuth } from "./auth";

// Route-level code splitting.
const LoginPage = lazy(() => import("./pages/Login"));
const InboxPage = lazy(() => import("./pages/Inbox"));
const ReportsPage = lazy(() => import("./pages/Reports"));
const CannedRepliesPage = lazy(() => import("./pages/CannedReplies"));

export default function App() {
  const { agent, logout } = useAuth();
  const location = useLocation();

  if (!agent && location.pathname !== "/login") {
    return <Navigate to="/login" replace />;
  }

  return (
    <div className="app">
      {agent && (
        <header className="topbar">
          <span className="brand">NetIntercom</span>
          <nav>
            <Link to="/">Inbox</Link>
            <Link to="/reports">Reports</Link>
            <Link to="/canned-replies">Canned replies</Link>
          </nav>
          <span className="spacer" />
          <span>{agent.displayName}</span>
          <button className="link" onClick={logout}>
            Sign out
          </button>
        </header>
      )}
      <Suspense fallback={<div className="page-loading">Loading…</div>}>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/" element={<InboxPage />} />
          <Route path="/reports" element={<ReportsPage />} />
          <Route path="/canned-replies" element={<CannedRepliesPage />} />
        </Routes>
      </Suspense>
    </div>
  );
}
