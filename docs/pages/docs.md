---
layout: page
title: Documentation
description: Browse all Melodee installation, administration, feature, CLI, and API documentation.
permalink: /docs/
tags:
  - documentation
  - reference
---

# Documentation

Browse the current {{ site.title }} documentation by subject. The version menu in
the site header switches between the current release and archived documentation.

<div class="section-index">
  {% for section in site.data.toc %}
    <hr class="panel-line">
    <h2><a href="{{ site.baseurl }}/{{ section.url }}/">{{ section.title }}</a></h2>
    {% for entry in section.links %}
      <div class="entry">
        <h5><a href="{{ site.baseurl }}/{{ entry.url }}/">{{ entry.title }}</a></h5>
      </div>
    {% endfor %}
  {% endfor %}
</div>
