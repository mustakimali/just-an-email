import { QRCodeSVG } from 'qrcode.react'

interface Props {
  value: string
  size?: number
}

export default function QRCode({ value, size = 200 }: Props) {
  return (
    <div className="flex justify-center">
      <QRCodeSVG value={value} size={size} bgColor="#d9edf7" fgColor="#000000" level="H" />
    </div>
  )
}
