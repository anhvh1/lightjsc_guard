import { Route, Routes } from 'react-router-dom';
import { AppShellLayout } from './components/AppShellLayout';
import { Attendance } from './pages/Attendance';
import { Cameras } from './pages/Cameras';
import { Dashboard } from './pages/Dashboard';
import { FaceStream } from './pages/FaceStream';
import { FaceTrace } from './pages/FaceTrace';
import { Maps } from './pages/Maps';
import { Persons } from './pages/Persons';
import { Sizing } from './pages/Sizing';
import { Settings } from './pages/Settings';
import { Subscribers } from './pages/Subscribers';
import { Welcome } from './pages/Welcome';

export default function App() {
  return (
    <AppShellLayout>
      <Routes>
        <Route path="/" element={<Dashboard />} />
        <Route path="/welcome" element={<Welcome />} />
        <Route path="/attendance" element={<Attendance />} />
        <Route path="/sizing" element={<Sizing />} />
        <Route path="/face-stream" element={<FaceStream />} />
        <Route path="/face-trace" element={<FaceTrace />} />
        <Route path="/maps" element={<Maps />} />
        <Route path="/cameras" element={<Cameras />} />
        <Route path="/persons" element={<Persons />} />
        <Route path="/subscribers" element={<Subscribers />} />
        <Route path="/settings" element={<Settings />} />
      </Routes>
    </AppShellLayout>
  );
}
