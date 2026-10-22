// Datele publice primite despre o persoană.
export interface PersonProfile {
  id: number;
  fullName: string;
  jobTitle: string;
  bio: string;
  skills: string;
  email: string | null;
}

// O pagină de rezultate din director.
export interface PeoplePage {
  items: PersonProfile[];
  page: number;
  pageSize: number;
  total: number;
}

// Datele editabile ale contului propriu.
export interface EditableProfile {
  fullName: string;
  jobTitle: string;
  bio: string;
  skills: string;
  isDiscoverable: boolean;
  showEmail: boolean;
}