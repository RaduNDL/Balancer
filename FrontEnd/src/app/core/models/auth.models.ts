// Rolurile efective dintr-o firmă.
export type CompanyRole =
  | 'Owner'
  | 'Admin'
  | 'Accountant'
  | 'Manager'
  | 'Employee';

export interface CompanyMembership {
  companyId: number;
  companyName: string;
  role: CompanyRole;
}

export interface AuthUser {
  id: number;
  fullName: string;
  email: string;
  memberships: CompanyMembership[];
}

export interface LoginRequest {
  email: string;
  password: string;
}

// Nu mai cerem numele unei firme la înregistrare.
export interface RegisterRequest {
  fullName: string;
  email: string;
  password: string;
  confirmPassword: string;
}

export interface MessageResponse {
  message: string;
}