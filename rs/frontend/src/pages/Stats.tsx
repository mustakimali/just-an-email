import { useState, useEffect } from 'react'
import { get } from '../api/client'
import type { StatYear } from '../types'

function formatBytes(bytes: number): string {
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB']
  if (bytes === 0) return '0 B'
  const i = Math.floor(Math.log(bytes) / Math.log(1024))
  return `${(bytes / Math.pow(1024, i)).toFixed(2)} ${sizes[i]}`
}

export default function Stats() {
  const [data, setData] = useState<StatYear[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    get<StatYear[]>('/api/stats/raw')
      .then(setData)
      .catch(console.error)
      .finally(() => setLoading(false))
  }, [])

  if (loading) return <div className="text-center py-12 text-gray-500">Loading...</div>

  return (
    <div className="max-w-4xl mx-auto px-4 py-8">
      <h1 className="text-2xl font-bold mb-6">Statistics</h1>
      {data.map((year) => (
        <div key={year.year} className="mb-8">
          <h2 className="text-xl font-semibold mb-4">20{year.year}</h2>
          {year.months.map((month) => (
            <div key={month.month} className="mb-4">
              <h3 className="text-lg font-medium mb-2 text-gray-600">Month {month.month}</h3>
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b text-left text-gray-500">
                      <th className="py-1 pr-4">Day</th>
                      <th className="py-1 pr-4">Sessions</th>
                      <th className="py-1 pr-4">Devices</th>
                      <th className="py-1 pr-4">Messages</th>
                      <th className="py-1 pr-4">Files</th>
                      <th className="py-1">Data</th>
                    </tr>
                  </thead>
                  <tbody>
                    {month.days.map((day) => {
                      const dayNum = day.Id % 100
                      return (
                        <tr key={day.Id} className="border-b border-gray-100">
                          <td className="py-1 pr-4">{dayNum || 'Total'}</td>
                          <td className="py-1 pr-4">{day.Sessions}</td>
                          <td className="py-1 pr-4">{day.Devices}</td>
                          <td className="py-1 pr-4">{day.Messages}</td>
                          <td className="py-1 pr-4">{day.Files}</td>
                          <td className="py-1">
                            {formatBytes(day.MessagesSizeBytes + day.FilesSizeBytes)}
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            </div>
          ))}
        </div>
      ))}
    </div>
  )
}
