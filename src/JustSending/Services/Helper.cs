namespace JustSending
{
    public static class Helper
    {
        public static string ToFileSizeString(this long fileSize)
        {
            var unitsOfFileSize = new[] { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            var fileSizeInLong = (double) fileSize;
            var order = 0;
            while (fileSizeInLong >= 1024 && order + 1 < unitsOfFileSize.Length)
            {
                order++;
                fileSizeInLong = fileSizeInLong / 1024;
            }

            return string.Format("{0:0.##} {1}", fileSizeInLong, unitsOfFileSize[order]);
        }
    }
}