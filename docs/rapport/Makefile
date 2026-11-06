MAIN = rapport

.PHONY: all clean

# tectonic télécharge automatiquement les paquets LaTeX manquants et gère bibtex.
all: $(MAIN).pdf

$(MAIN).pdf: $(MAIN).tex preamble.tex titlepage.tex references.bib \
             $(wildcard frontmatter/*.tex) $(wildcard chapters/*.tex) $(wildcard annexes/*.tex)
	tectonic --keep-logs $(MAIN).tex

clean:
	rm -f *.aux *.log *.toc *.lof *.lot *.out *.bbl *.blg
