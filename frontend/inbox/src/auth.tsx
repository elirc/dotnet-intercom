import { createContext, useContext, useState, type ReactNode } from "react";
import { post, setTokens } from "./api";
import type { AgentDto, AuthResult } from "./types";

interface AuthContextValue {
  agent: AgentDto | null;
  login: (email: string, password: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [agent, setAgent] = useState<AgentDto | null>(() => {
    const saved = sessionStorage.getItem("agent");
    return saved ? (JSON.parse(saved) as AgentDto) : null;
  });

  async function login(email: string, password: string) {
    const auth = await post<AuthResult>("/auth/login", { email, password });
    setTokens(auth);
    sessionStorage.setItem("agent", JSON.stringify(auth.agent));
    setAgent(auth.agent);
  }

  function logout() {
    setTokens(null);
    sessionStorage.removeItem("agent");
    setAgent(null);
  }

  return <AuthContext.Provider value={{ agent, login, logout }}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside AuthProvider");
  return ctx;
}
