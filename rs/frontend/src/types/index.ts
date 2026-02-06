export interface Message {
  Id: string
  SessionId: string
  SessionIdVerification?: string
  SocketConnectionId?: string
  EncryptionPublicKeyAlias?: string
  Text: string
  FileName?: string
  DateSent: string
  HasFile: boolean
  FileSizeBytes?: number
  IsNotification: boolean
  DateSentEpoch: number
}

export interface ServerMessage {
  type: string
  connectionId?: string
  token?: string
  count?: number
  peerId?: string
  pka?: string
  initiate?: boolean
  method?: string
  data?: string
}

export interface LitePollResponse {
  hasSession: boolean
  hasToken: boolean
  token?: string
  messages: Message[]
}

export interface StatYear {
  year: string
  months: StatMonth[]
}

export interface StatMonth {
  month: string
  days: StatDay[]
}

export interface StatDay {
  Id: number
  Messages: number
  MessagesSizeBytes: number
  Files: number
  FilesSizeBytes: number
  Devices: number
  Sessions: number
}
