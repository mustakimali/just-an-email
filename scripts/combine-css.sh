#!/bin/bash
# combined multiple css files (remote and local) and creates a single file.
# optionally minified using uglifycss (if already installed)
# $ sudo npm install uglifycss -g
# usage:
# ./combine-css.sh <.cshtml file to parser> <out folder> <name>
# example:
# ./combine-css.sh _CommonHead.cshtml src/JustSending/wwwroot/css combined-main
#
# creates src/JustSending/wwwroot/css/combined-main.min.css (if minified)
#      or src/JustSending/wwwroot/css/combined-main.css (if not minified)

set -eu

grey='\e[2m'
red='\e[91m'
yellow='\e[93m'
bold='\e[1m'
italic='\e[3m'
reset='\e[0m'

INPUT_FILE=$1
OUTPUT_PATH=$2
OUTPUT_FILE=$3

# OUTPUT_PATH="src/JustSending/wwwroot/css"
# OUTPUT_FILE="combined-main"

OUTPUT_FILENAME="$OUTPUT_PATH/$OUTPUT_FILE.css"
OUTPUT_FILENAME_MIN="$OUTPUT_PATH/$OUTPUT_FILE.min.css"

echo Getting list
SESSION_EXTERNAL=($(cat $INPUT_FILE | sed 's/@@/@/g' | grep -oP "(https[^\"]*.css)"))
SESSION_INTERNAL=($(cat $INPUT_FILE | sed 's/@@/@/g' | grep "<link rel" | grep -oP "(~[^\"]*.css)" | sed 's/\~/src\/JustSending\/wwwroot/g'))

echo "Clearing any existing file @ $OUTPUT_FILENAME"
rm $OUTPUT_FILENAME || true

# download and cat external css
echo "Combining ${#SESSION_EXTERNAL[@]} external script(s)..."
for i in "${SESSION_EXTERNAL[@]}"
do
   echo "Downloading: $i..."
   echo "" >> $OUTPUT_FILENAME
   echo "/* Source: $i */" >> $OUTPUT_FILENAME
   curl -s $i >> $OUTPUT_FILENAME
done

# internal css
echo "Combining ${#SESSION_INTERNAL[*]} internal script(s)..."
for i in "${SESSION_INTERNAL[@]}"
do
   echo "Combining: $i..."
   echo "" >> $OUTPUT_FILENAME
   echo "/* Source: $i */" >> $OUTPUT_FILENAME
   cat $i >> $OUTPUT_FILENAME
done

# fix link for font
sed -i 's/..\/fonts/https\:\/\/cdnjs.cloudflare.com\/ajax\/libs\/font-awesome\/4.7.0\/fonts/g' $OUTPUT_FILENAME
# fix for this weird character being prepended in local file
sed -i 's/﻿//g' $OUTPUT_FILENAME

ls -lsah $OUTPUT_FILENAME

if command -v uglifycss &> /dev/null
then
    # minify
    echo "Minifing..."
    uglifycss $OUTPUT_FILENAME > $OUTPUT_FILENAME_MIN
    rm $OUTPUT_FILENAME
    ls -lsah $OUTPUT_FILENAME_MIN
else
    echo -e "${red}Could not minify the output."
    echo -e "Install uglifycss and try again.$reset"
    echo "$ sudo npm install uglifycss -g"
fi

function show_hash()
{
    file=$1
    name=$2
    hash=$(openssl dgst -sha384 -binary $file | openssl base64 -A)
    # `rel` is after `href` so this won't be picked up
    # by this script to be minified
    echo -e "$yellow<link href=\"$name\" rel=\"stylesheet\" asp-append-version=\"true\" integrity=\"sha384-$hash\"/>$reset"
    
}

show_hash $OUTPUT_FILENAME "~/css/$OUTPUT_FILE.css"
show_hash $OUTPUT_FILENAME_MIN "~/css/$OUTPUT_FILE.min.css" 
