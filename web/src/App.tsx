import { BrowserRouter, NavLink, Route, Routes } from 'react-router-dom'
import { IS_DEMO } from './api'
import { DemoBanner } from './demo/DemoParts'
import { DashboardPage } from './pages/DashboardPage'
import { ReceiptsPage } from './pages/ReceiptsPage'
import { ReviewPage } from './pages/ReviewPage'
import { UploadPage } from './pages/UploadPage'

export default function App() {
  return (
    <BrowserRouter>
      <header className="app-header">
        <NavLink to="/" className="brand">
          🧾 Receipt Tracker
        </NavLink>
        <nav>
          <NavLink to="/" end>
            Spending
          </NavLink>
          <NavLink to="/receipts">Receipts</NavLink>
          <NavLink to="/upload" className="nav-add">
            + Add
          </NavLink>
        </nav>
      </header>
      {IS_DEMO && <DemoBanner />}

      <main>
        <Routes>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/upload" element={<UploadPage />} />
          <Route path="/receipts" element={<ReceiptsPage />} />
          <Route path="/receipts/:id" element={<ReviewPage />} />
          <Route path="*" element={<p className="empty">Page not found.</p>} />
        </Routes>
      </main>

      <footer className="app-footer">
        {IS_DEMO ? "Demo site: nothing you do here leaves your browser." : "Runs entirely on your computer — receipts never leave it."}
      </footer>
    </BrowserRouter>
  )
}
