import { QRCodeSVG } from 'qrcode.react'

interface Props {
  token: string
  onCancel: () => void
  sessionUrl: string
}

export default function SharePanel({ token, onCancel, sessionUrl }: Props) {
  return (
    <div className="share-panel">
      <h2 className="text-white text-xl mb-1">Connect Another Device</h2>
      <p className="text-white/70 text-sm mb-4">
        Enter this code on another device, or scan the QR code
      </p>
      <div className="token mb-4">{token}</div>
      <div className="flex justify-center mb-4">
        <QRCodeSVG
          value={sessionUrl}
          size={200}
          bgColor="#ffffff"
          fgColor="#000000"
          level="H"
        />
      </div>
      <button
        onClick={onCancel}
        className="btn border border-white/30 text-white hover:bg-white/10"
      >
        Cancel
      </button>
    </div>
  )
}
