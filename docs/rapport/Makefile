MAIN = rapport

.PHONY: all clean

# pdflatex (TeX Live ou MiKTeX) + bibtex : trois passes pour les references croisees,
# la bibliographie et la liste des acronymes.
all: $(MAIN).pdf

$(MAIN).pdf: $(MAIN).tex preamble.tex titlepage.tex references.bib \
             $(wildcard frontmatter/*.tex) $(wildcard chapters/*.tex) $(wildcard annexes/*.tex)
	pdflatex -interaction=nonstopmode -halt-on-error $(MAIN).tex
	bibtex $(MAIN)
	pdflatex -interaction=nonstopmode -halt-on-error $(MAIN).tex
	pdflatex -interaction=nonstopmode -halt-on-error $(MAIN).tex

clean:
	rm -f *.aux *.log *.toc *.lof *.lot *.out *.bbl *.blg
