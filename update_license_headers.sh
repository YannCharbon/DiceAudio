# Adds the GPL header to every source file that does not have one yet.
# Mirrors the DiceCombats repository so both projects stay aligned.
find . -type f -name "*.cs" -not -path "./*/obj/*" -not -path "./*/bin/*" -print0 | xargs -0 add-header --header-filepath "license_template.txt" --comment-style "/*|| */
" --newline-after-comment-start --newline-before-comment-end
find . -type f -name "*.razor" -not -path "./*/obj/*" -not -path "./*/bin/*" -print0 | xargs -0 add-header --header-filepath "license_template.txt" --comment-style "@*||*@
" --newline-after-comment-start --newline-before-comment-end
