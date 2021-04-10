#!/bin/bash
set -eu

# ./combine-scripts.sh src/JustSending/Views/Shared/_Layout.cshtml src/JustSending/wwwroot/js combined-main
# ./combine-scripts.sh src/JustSending/Views/Shared/_Layout.cshtml src/JustSending/wwwroot/js combined-session

grey='\e[2m'
red='\e[91m'
yellow='\e[93m'
bold='\e[1m'
italic='\e[3m'
reset='\e[0m'

INPUT_FILE=$1
OUTPUT_PATH=$2
OUTPUT_FILE=$3

# OUTPUT_PATH="src/JustSending/wwwroot/js"
# OUTPUT_FILE="combined-session"

OUTPUT_FILENAME="$OUTPUT_PATH/$OUTPUT_FILE.js"
OUTPUT_FILENAME_MIN="$OUTPUT_PATH/$OUTPUT_FILE.min.js"

echo Getting list
SESSION_EXTERNAL=($(cat $INPUT_FILE | grep "script src" | sed 's/@@/@/g' | awk -F "\"" '{print $2}' | grep https))
SESSION_INTERNAL=($(cat $INPUT_FILE | grep "script src" | sed 's/@@/@/g' | awk -F "\"" '{print $2}' | grep '~/' | sed 's/\~/src\/JustSending\/wwwroot/g'))

echo "Clearing any existing file @ $OUTPUT_FILENAME"
rm $OUTPUT_FILENAME || true

# download and cat external scripts
echo "Combining ${#SESSION_EXTERNAL[@]} external script(s)..."
for i in "${SESSION_EXTERNAL[@]}"
do
   echo "Downloading: $i..."
   echo "" >> $OUTPUT_FILENAME
   echo "/* Source: $i */" >> $OUTPUT_FILENAME
   curl -s $i >> $OUTPUT_FILENAME
done

# internal scripts
echo "Combining ${#SESSION_INTERNAL[*]} internal script(s)..."
for i in "${SESSION_INTERNAL[@]}"
do
   echo "Combining: $i..."
   echo "" >> $OUTPUT_FILENAME
   echo "/* Source: $i */" >> $OUTPUT_FILENAME
   cat $i >> $OUTPUT_FILENAME
done

# fix for this weird character being prepended in local file
sed -i 's/﻿//g' $OUTPUT_FILENAME

ls -lsah $OUTPUT_FILENAME

if command -v terser &> /dev/null
then
    # minify
    echo "Minifing..."
    terser $OUTPUT_FILENAME > $OUTPUT_FILENAME_MIN
    rm $OUTPUT_FILENAME
    ls -lsah $OUTPUT_FILENAME_MIN
else
    echo -e "${red}Could not minify the output."
    echo -e "Install terser and try again.$reset"
    echo "$ sudo npm install terser -g"
fi

function show_hash()
{
    file=$1
    name=$2
    hash=$(openssl dgst -sha384 -binary $file | openssl base64 -A)
    echo -e "$yellow<script type=\"text/javascript\" src=\"$name\" asp-append-version=\"true\" integrity=\"sha384-$hash\" crossorigin=\"anonymous\"></script>$reset"
}

#show_hash $OUTPUT_FILENAME "~/js/$OUTPUT_FILE.js"
show_hash $OUTPUT_FILENAME_MIN "~/js/$OUTPUT_FILE.min.js" 
